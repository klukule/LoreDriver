// Copyright Epic Games, Inc. All Rights Reserved.

using System.Runtime.CompilerServices;
using EpicGames.Core;
using EpicGames.Horde.Commits;
using EpicGames.Horde.Streams;
using EpicGames.Horde.Users;
using Grpc.Core;
using Grpc.Net.Client;
using HordeServer.Streams;
using HordeServer.Users;
using HordeServer.VersionControl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using ByteString = Google.Protobuf.ByteString;
using LoreModelV1 = global::Lore.Model.V1;
using LoreRepositoryV1 = global::Lore.Repository.V1;
using LoreRevisionV1 = global::Lore.Revision.V1;
using LoreThinClientV1 = global::Lore.ThinClient.V1;

namespace HordeServer.VersionControl.Lore
{
	/// <summary>
	/// Version control service for Lore.
	/// Reads commit metadata from the Lore server over gRPC API.
	/// </summary>
	public sealed class LoreService : IVersionControlService
	{
		readonly IUserCollection _userCollection;
		readonly IOptionsMonitor<LoreConfig> _loreConfig;
		readonly ILogger _logger;
		readonly ConcurrentDictionary<string, GrpcChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

		/// <inheritdoc/>
		public string Name => LoreUtils.VcsName;

		/// <summary>
		/// Constructor
		/// </summary>
		public LoreService(IUserCollection userCollection, IOptionsMonitor<LoreConfig> loreConfig, ILogger<LoreService> logger)
		{
			_userCollection = userCollection;
			_loreConfig = loreConfig;
			_logger = logger;
		}

		/// <inheritdoc/>
		public ICommitCollection GetCommits(StreamConfig streamConfig) => new LoreCommitCollection(this, streamConfig);

		/// <summary>
		/// Resolves a commit to its Lore revision signature (lowercase hex), which the agent materializes.
		/// </summary>
		public async Task<string?> TryGetRevisionHashAsync(StreamConfig streamConfig, CommitId commitId, CancellationToken cancellationToken = default)
		{
			try
			{
				return await ((LoreCommitCollection)GetCommits(streamConfig)).GetSignatureHexAsync(commitId, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Unable to resolve Lore revision hash for stream {StreamId} commit {CommitId}", streamConfig.Id, commitId);
				return null;
			}
		}

		internal IUserCollection UserCollection => _userCollection;

		internal GrpcChannel GetChannel(StreamConfig streamConfig)
		{
			LoreClusterConfig cluster = _loreConfig.CurrentValue.FindClusterForStream(streamConfig) ?? throw new CommitCollectionException($"No Lore cluster configured for stream {streamConfig.Id}", null);
			return _channels.GetOrAdd(cluster.ServerAndPort, addr => GrpcChannel.ForAddress(ToGrpcAddress(addr)));
		}

		// lore://host:port -> http://host:port (gRPC over plaintext h2c for a no-TLS local server). Use https:// for a TLS-enabled Lore gRPC endpoint.
		static string ToGrpcAddress(string serverAndPort)
		{
			// TODO: Support HTTPS for TLS-enabled servers with valid certificates.
			string value = serverAndPort.Trim();
			int idx = value.IndexOf("://", StringComparison.Ordinal);
			if (idx >= 0)
			{
				value = value[(idx + 3)..];
			}
			return $"http://{value.TrimEnd('/')}";
		}
	}

	/// <summary>
	/// Commit collection for Lore streams.
	/// </summary>
	sealed class LoreCommitCollection : ICommitCollection
	{
		// gRPC metadata key carrying the target repository id (binary). Every revision/branch call must include it.
		const string RepositoryIdKey = "urc-repository-id-bin";

		readonly LoreService _owner;
		readonly StreamConfig _streamConfig;
		readonly LoreRepositoryV1.RepositoryService.RepositoryServiceClient _repositories;
		readonly LoreRevisionV1.RevisionService.RevisionServiceClient _revisions;
		readonly LoreThinClientV1.ThinClientService.ThinClientServiceClient _thinClient;
		readonly SemaphoreSlim _headerLock = new(1, 1);
		Metadata? _headers;

		public LoreCommitCollection(LoreService owner, StreamConfig streamConfig)
		{
			_owner = owner;
			_streamConfig = streamConfig;
			GrpcChannel channel = owner.GetChannel(streamConfig);
			_repositories = new LoreRepositoryV1.RepositoryService.RepositoryServiceClient(channel);
			_revisions = new LoreRevisionV1.RevisionService.RevisionServiceClient(channel);
			_thinClient = new LoreThinClientV1.ThinClientService.ThinClientServiceClient(channel);
		}

		string BranchName => LoreUtils.GetBranch(_streamConfig);
		string RepositoryName => _streamConfig.RepositoryName ?? _streamConfig.Name;

		// Resolves the repository id (by name) once and caches the gRPC header carrying it
		async Task<Metadata> GetHeadersAsync(CancellationToken cancellationToken)
		{
			if (_headers != null)
			{
				return _headers;
			}
			await _headerLock.WaitAsync(cancellationToken);
			try
			{
				if (_headers == null)
				{
					LoreRepositoryV1.RepositoryGetResponse response = await _repositories.RepositoryGetAsync(new LoreRepositoryV1.RepositoryGetRequest { Name = RepositoryName }, cancellationToken: cancellationToken);
					_headers = new Metadata { { RepositoryIdKey, response.Repository.Id.ToByteArray() } };
				}
			}
			finally
			{
				_headerLock.Release();
			}
			return _headers;
		}

		/// <inheritdoc/>
		public Task<CommitIdWithOrder> CreateNewAsync(string path, string description, CancellationToken cancellationToken = default) => throw new CommitCollectionException("Creating new commits is not supported for Lore streams", null);

		/// <inheritdoc/>
		public async Task<ICommit> GetAsync(CommitId commitId, CancellationToken cancellationToken = default)
		{
			ByteString branchId = await GetBranchIdAsync(cancellationToken);
			long order = OrderOf(commitId);
			LoreThinClientV1.Revision revision = await GetByNumberAsync(branchId, order > 0 ? (ulong)order : 0UL, cancellationToken);
			return await BuildCommitAsync(revision, cancellationToken);
		}

		/// <inheritdoc/>
		public ValueTask<CommitIdWithOrder> GetOrderedAsync(CommitId commitId, CancellationToken cancellationToken = default)
		{
			if (commitId is CommitIdWithOrder ordered)
			{
				return new ValueTask<CommitIdWithOrder>(ordered);
			}
			long order = OrderOf(commitId);
			return new ValueTask<CommitIdWithOrder>(new CommitIdWithOrder(commitId.Name, (int)order));
		}

		/// <inheritdoc/>
		public async IAsyncEnumerable<ICommit> FindAsync(CommitId? minCommitId = null, bool includeMinCommit = true, CommitId? maxCommitId = null, bool includeMaxCommit = true, int? maxResults = null, IReadOnlyList<CommitTag>? tags = null, CommitSortOrder sortOrder = CommitSortOrder.Descending, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			ByteString branchId = await GetBranchIdAsync(cancellationToken);
			long head = (long)(await GetByNumberAsync(branchId, 0, cancellationToken)).Number;

			long lo = minCommitId != null ? OrderOf(minCommitId) : 1;
			long hi = maxCommitId != null ? OrderOf(maxCommitId) : head;
			if (!includeMinCommit) { lo++; }
			if (!includeMaxCommit) { hi--; }
			lo = Math.Max(lo, 1);
			hi = Math.Min(hi, head);

			int count = 0;
			if (sortOrder == CommitSortOrder.Ascending)
			{
				for (long number = lo; number <= hi; number++)
				{
					ICommit? commit = await EmitAsync(branchId, number, tags, cancellationToken);
					if (commit != null)
					{
						yield return commit;
						if (maxResults != null && ++count >= maxResults.Value) { yield break; }
					}
				}
			}
			else
			{
				for (long number = hi; number >= lo; number--)
				{
					ICommit? commit = await EmitAsync(branchId, number, tags, cancellationToken);
					if (commit != null)
					{
						yield return commit;
						if (maxResults != null && ++count >= maxResults.Value) { yield break; }
					}
				}
			}
		}

		/// <inheritdoc/>
		public async IAsyncEnumerable<ICommit> SubscribeAsync(CommitId minCommitId, IReadOnlyList<CommitTag>? tags = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			ByteString branchId = await GetBranchIdAsync(cancellationToken);
			long last = OrderOf(minCommitId);

			while (!cancellationToken.IsCancellationRequested)
			{
				long head = (long)(await GetByNumberAsync(branchId, 0, cancellationToken)).Number;
				if (head > last)
				{
					for (long number = last + 1; number <= head; number++)
					{
						ICommit? commit = await EmitAsync(branchId, number, tags, cancellationToken);
						last = number;
						if (commit != null)
						{
							yield return commit;
						}
					}
				}
				else
				{
					await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
				}
			}
		}

		async Task<ICommit?> EmitAsync(ByteString branchId, long number, IReadOnlyList<CommitTag>? tags, CancellationToken cancellationToken)
		{
			LoreThinClientV1.Revision revision = await GetByNumberAsync(branchId, (ulong)number, cancellationToken);
			ICommit commit = await BuildCommitAsync(revision, cancellationToken);
			if (tags is { Count: > 0 })
			{
				IReadOnlyList<CommitTag> commitTags = await commit.GetTagsAsync(cancellationToken);
				if (!tags.Any(commitTags.Contains))
				{
					return null;
				}
			}
			return commit;
		}

		static long OrderOf(CommitId commitId)
		{
			if (commitId is CommitIdWithOrder ordered)
			{
				return ordered.Order;
			}
			return Int64.TryParse(commitId.Name, out long order) ? order : 0;
		}

		async Task<ByteString> GetBranchIdAsync(CancellationToken cancellationToken)
		{
			Metadata headers = await GetHeadersAsync(cancellationToken);
			LoreRevisionV1.BranchGetResponse response = await _revisions.BranchGetAsync(new LoreRevisionV1.BranchGetRequest { Name = BranchName }, headers, cancellationToken: cancellationToken);
			return response.Branch.Id;
		}

		// Resolves a commit to the lowercase-hex signature the agent materializer syncs to
		internal async Task<string> GetSignatureHexAsync(CommitId commitId, CancellationToken cancellationToken)
		{
			ByteString branchId = await GetBranchIdAsync(cancellationToken);
			long order = OrderOf(commitId);
			LoreThinClientV1.Revision revision = await GetByNumberAsync(branchId, order > 0 ? (ulong)order : 0UL, cancellationToken);
			return Convert.ToHexString(revision.Signature.ToByteArray()).ToLowerInvariant();
		}

		// number == 0 resolves to the branch head
		async Task<LoreThinClientV1.Revision> GetByNumberAsync(ByteString branchId, ulong number, CancellationToken cancellationToken)
		{
			Metadata headers = await GetHeadersAsync(cancellationToken);
			LoreThinClientV1.RevisionInfoRequest request = new() { Identifier = new LoreModelV1.RevisionIdentifier { BranchId = branchId, Number = number } };
			LoreThinClientV1.RevisionInfoResponse response = await _thinClient.RevisionInfoAsync(request, headers, cancellationToken: cancellationToken);
			return response.Revision;
		}

		async Task<ICommit> BuildCommitAsync(LoreThinClientV1.Revision revision, CancellationToken cancellationToken)
		{
			string createdBy = String.IsNullOrEmpty(revision.CreatedBy) ? "unknown" : revision.CreatedBy;
			string committedBy = String.IsNullOrEmpty(revision.CommittedBy) ? createdBy : revision.CommittedBy;
			// Lore stores the commit timestamp in milliseconds.
			DateTime dateUtc = revision.Timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)revision.Timestamp).UtcDateTime : DateTime.UnixEpoch;

			IUser author = await _owner.UserCollection.FindOrAddUserByLoginAsync(createdBy, null, null, cancellationToken);
			IUser owner = String.Equals(committedBy, createdBy, StringComparison.Ordinal) ? author : await _owner.UserCollection.FindOrAddUserByLoginAsync(committedBy, null, null, cancellationToken);

			Metadata headers = await GetHeadersAsync(cancellationToken);
			ByteString? parent = revision.ParentSelf?.Signature;
			return new LoreCommit(_thinClient, headers, _streamConfig, (int)revision.Number, revision.Signature, author.Id, owner.Id, revision.CommitMessage, dateUtc, parent);
		}
	}

	/// <summary>
	/// A single Lore commit, identified by its revision number
	/// </summary>
	sealed class LoreCommit : ICommit
	{
		readonly LoreThinClientV1.ThinClientService.ThinClientServiceClient _thinClient;
		readonly Metadata _headers;
		readonly StreamConfig _streamConfig;
		readonly ByteString _signature;
		readonly ByteString? _parent;

		IReadOnlyList<string>? _files;

		public CommitIdWithOrder Id { get; }
		public StreamId StreamId => _streamConfig.Id;
		public CommitIdWithOrder OriginalCommitId => Id;
		public UserId AuthorId { get; }
		public UserId OwnerId { get; }
		public string Description { get; }
		public string BasePath => String.Empty;
		public DateTime DateUtc { get; }

		public LoreCommit(LoreThinClientV1.ThinClientService.ThinClientServiceClient thinClient, Metadata headers, StreamConfig streamConfig, int number, ByteString signature, UserId authorId, UserId ownerId, string description, DateTime dateUtc, ByteString? parent)
		{
			_thinClient = thinClient;
			_headers = headers;
			_streamConfig = streamConfig;
			_signature = signature;
			_parent = parent;

			Id = new CommitIdWithOrder(number.ToString(System.Globalization.CultureInfo.InvariantCulture), number);
			AuthorId = authorId;
			OwnerId = ownerId;
			Description = description;
			DateUtc = dateUtc;
		}

		public async ValueTask<IReadOnlyList<CommitTag>> GetTagsAsync(CancellationToken cancellationToken)
		{
			List<CommitTag> commitTags = new();
			foreach (CommitTagConfig commitTagConfig in _streamConfig.GetAllCommitTags())
			{
				if (_streamConfig.TryGetCommitTagFilter(commitTagConfig.Name, out FileFilter? filter) && await MatchesFilterAsync(filter, cancellationToken))
				{
					commitTags.Add(commitTagConfig.Name);
				}
			}
			return commitTags;
		}

		public async ValueTask<bool> MatchesFilterAsync(FileFilter filter, CancellationToken cancellationToken)
		{
			IReadOnlyList<string> files = await GetFilesAsync(null, null, cancellationToken);
			return filter.ApplyTo(files).Any();
		}

		public async ValueTask<IReadOnlyList<string>> GetFilesAsync(int? minFiles, int? maxFiles, CancellationToken cancellationToken)
		{
			if (_files == null)
			{
				if (_parent == null)
				{
					_files = Array.Empty<string>();
				}
				else
				{
					List<string> files = new();
					LoreThinClientV1.RevisionDiffRequest request = new()
					{
						SignatureFrom = _parent,
						SignatureTo = _signature,
					};
					using AsyncServerStreamingCall<LoreThinClientV1.RevisionDiffResponse> call = _thinClient.RevisionDiff(request, _headers, cancellationToken: cancellationToken);
					await foreach (LoreThinClientV1.RevisionDiffResponse response in call.ResponseStream.ReadAllAsync(cancellationToken))
					{
						if (response.PayloadCase == LoreThinClientV1.RevisionDiffResponse.PayloadOneofCase.Change)
						{
							files.Add(response.Change.Path);
						}
					}
					_files = files;
				}
			}

			IReadOnlyList<string> result = _files;
			if (maxFiles.HasValue && result.Count > maxFiles.Value)
			{
				result = result.Take(maxFiles.Value).ToArray();
			}
			return result;
		}
	}
}
