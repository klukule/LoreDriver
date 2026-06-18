// Copyright Epic Games, Inc. All Rights Reserved.

using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json;
using System.Web;
using EpicGames.Core;
using HordeCommon.Rpc.Messages;
using JobDriver.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using LoreVcs.Types;
using LoreVcs.Types.Args;
using LoreVcs.Types.Enums;
using LoreVcs.Types.Events;
using Lore = LoreVcs.Lore;

namespace LoreDriver;

/// <summary>
/// Options for <see cref="LoreMaterializer" />
/// </summary>
/// <param name="DirPath">Base working directory for the agent</param>
/// <param name="AgentWorkspace">Workspace options</param>
/// <param name="Branch">Branch to sync</param>
/// <param name="Offline">Whether to operate without contacting the remote server</param>
public record LoreMaterializerOptions(string DirPath, RpcAgentWorkspace AgentWorkspace, string Branch, bool Offline)
{
	/// <summary>
	/// Remote repository URL, composed from the server address and stream
	/// </summary>
	public string RepositoryUrl
	{
		get
		{
			string stream = AgentWorkspace.Stream ?? String.Empty;
			if (stream.Contains("://", StringComparison.Ordinal))
			{
				return stream;
			}

			string server = AgentWorkspace.ServerAndPort ?? String.Empty;
			return String.IsNullOrEmpty(server) ? stream : $"{server.TrimEnd('/')}/{stream.TrimStart('/')}";
		}
	}

	/// <summary>
	/// Revision to sync, for hash-based lookups. Empty syncs to the branch HEAD.
	/// </summary>
	public string Revision => AgentWorkspace.RevisionHash ?? String.Empty;

	/// <summary>
	/// Parse options from <see cref="RpcAgentWorkspace.Method" /> as an HTTP query string
	/// </summary>
	public static LoreMaterializerOptions FromMethodString(string dirPath, RpcAgentWorkspace agentWorkspace)
	{
		string branch = "main";
		bool offline = false;
		if (!String.IsNullOrEmpty(agentWorkspace.Method))
		{
			NameValueCollection nameValues = HttpUtility.ParseQueryString(agentWorkspace.Method);
			if (String.Equals(nameValues["name"], LoreMaterializer.TypeName, StringComparison.OrdinalIgnoreCase))
			{
				branch = nameValues["branch"] ?? branch;
				offline = String.Equals(nameValues["offline"], "true", StringComparison.OrdinalIgnoreCase);
			}
		}
		return new LoreMaterializerOptions(dirPath, agentWorkspace, branch, offline);
	}
}

/// <summary>
/// Materializer using the Lore SDK for syncing files
/// </summary>
public sealed class LoreMaterializer : IWorkspaceMaterializer
{
	/// <summary>
	/// Name of this materializer
	/// </summary>
	public const string TypeName = "Lore";

	internal const int CurrentStateVersion = 1;
	internal enum TransactionStatus { Clean = 0, Dirty = 1 }

	internal record State(int Version, TransactionStatus Status, string Identifier, string RepositoryUrl, string Branch, string? Revision);

	/// <inheritdoc/>
	public DirectoryReference SyncDir { get; }

	/// <inheritdoc/>
	public DirectoryReference BaseDir { get; }

	/// <inheritdoc/>
	public string Name => TypeName;

	/// <inheritdoc/>
	public string Identifier { get; }

	/// <inheritdoc/>
	public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

	/// <inheritdoc/>
	public bool IsPerforceWorkspace => false;

	private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
	private static int s_logConfigured;

	private readonly LoreMaterializerOptions _options;
	private readonly Tracer _tracer;
	private readonly ILogger _logger;
	private readonly DirectoryReference _metadataDir;
	private readonly string _stateFile;
	private readonly string _stateTempFile;
	private readonly IDisposable? _logSubscription;
	private const string StateFilename = "State.json";

	/// <summary>
	/// Constructor
	/// </summary>
	public LoreMaterializer(LoreMaterializerOptions options, Tracer tracer, ILogger logger)
	{
		_options = options;
		_tracer = tracer;
		_logger = logger;
		Identifier = options.AgentWorkspace.Identifier;
		_metadataDir = DirectoryReference.Combine(new DirectoryReference(options.DirPath), Identifier);
		SyncDir = DirectoryReference.Combine(_metadataDir, "Sync");
		BaseDir = _metadataDir;
		_stateFile = Path.Join(_metadataDir.FullName, StateFilename);
		_stateTempFile = Path.Join(_metadataDir.FullName, StateFilename + ".tmp");

		EnvironmentVariables = new Dictionary<string, string>
		{
			["UE_HORDE_LORE_REPOSITORY"] = options.RepositoryUrl,
			["UE_HORDE_LORE_BRANCH"] = options.Branch,
		};

		if (Interlocked.Exchange(ref s_logConfigured, 1) == 0)
		{
			Lore.LogConfigure(new LoreLogConfig { Level = LoreLogLevel.DEBUG });
		}
		_logSubscription = Lore.GlobalCallback(LoreEventTag.LOG, ForwardLogEvent);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_logSubscription?.Dispose();
	}

	/// <inheritdoc/>
	public ILogger GetLogger(ILogger logger) => logger;

	/// <inheritdoc/>
	public async Task SyncAsync(int changeNum, int shelveChangeNum, SyncOptions options, CancellationToken cancellationToken)
	{
		using TelemetrySpan span = _tracer.StartActiveSpan($"{nameof(LoreMaterializer)}.{nameof(SyncAsync)}");

		if (shelveChangeNum > 0)
		{
			_logger.LogWarning("Lore does not support shelved/preflight changes; ignoring shelve {Shelve}", shelveChangeNum);
		}

		string url = _options.RepositoryUrl;
		string branch = _options.Branch;
		// The server stamps the Lore revision signature onto the workspace; sync to it. Empty syncs to the branch tip.
		string? revision = String.IsNullOrEmpty(_options.Revision) ? null : _options.Revision;

		State? state = await LoadStateAsync(cancellationToken);
		bool isDirty = state == null
			|| state.Version != CurrentStateVersion
			|| state.Status == TransactionStatus.Dirty
			|| state.Identifier != Identifier
			|| !String.Equals(state.RepositoryUrl, url, StringComparison.Ordinal)
			|| !DirectoryReference.Exists(SyncDir);

		_logger.LogInformation("Lore sync: Url={Url} Branch={Branch} Revision={Revision} Dirty={Dirty}", url, branch, revision ?? "(tip)", isDirty);

		using LoreGlobalArgs global = CreateGlobalArgs();

		if (isDirty)
		{
			if (DirectoryReference.Exists(SyncDir))
			{
				FileUtils.ForceDeleteDirectory(SyncDir, usePosixFallback: true, _logger);
			}
			Directory.CreateDirectory(SyncDir.FullName);
			await SaveStateAsync(new State(CurrentStateVersion, TransactionStatus.Dirty, Identifier, url, branch, revision), cancellationToken);

			using LoreRepositoryCloneArgs cloneArgs = new() { RepositoryUrl = url };
			Stopwatch timer = Stopwatch.StartNew();
			_logger.LogInformation("Cloning {Url} into {Dir}...", url, SyncDir.FullName);
			Check("clone", await Lore.RepositoryClone(global, cloneArgs).WaitAsync());
			_logger.LogInformation("Cloned in {Time:0.0}s", timer.Elapsed.TotalSeconds);
		}

		using LoreBranchSwitchArgs switchArgs = new() { Branch = branch };
		Check("branch switch", await Lore.BranchSwitch(global, switchArgs).WaitAsync());

		if (revision != null)
		{
			using LoreRevisionSyncArgs syncArgs = new() { Revision = revision };
			Check("revision sync", await Lore.RevisionSync(global, syncArgs).WaitAsync());
		}

		string? syncedRevision = await TryGetRevisionAsync(global, cancellationToken) ?? revision;
		await SaveStateAsync(new State(CurrentStateVersion, TransactionStatus.Clean, Identifier, url, branch, syncedRevision), cancellationToken);
	}

	/// <inheritdoc/>
	public async Task FinalizeAsync(CancellationToken cancellationToken)
	{
		if (!DirectoryReference.Exists(SyncDir))
		{
			return;
		}

		try
		{
			using LoreGlobalArgs global = CreateGlobalArgs();
			using LoreRepositoryFlushArgs flushArgs = new();
			Check("flush", await Lore.RepositoryFlush(global, flushArgs).WaitAsync());
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Lore flush failed during finalize");
		}
	}

	/// <inheritdoc/>
	public async Task ConformAsync(bool removeUntrackedFiles, CancellationToken cancellationToken)
	{
		using TelemetrySpan span = _tracer.StartActiveSpan($"{nameof(LoreMaterializer)}.{nameof(ConformAsync)}");

		// Force a clean by discarding any persisted state - TODO: Check for better method (like force sync or something)
		FileUtils.ForceDeleteFile(_stateFile);
		await SyncAsync(IWorkspaceMaterializer.LatestChangeNumber, 0, new SyncOptions { RemoveUntracked = removeUntrackedFiles }, cancellationToken);
	}

	private LoreGlobalArgs CreateGlobalArgs() => new() { RepositoryPath = SyncDir.FullName, Offline = _options.Offline };

	private async Task<string?> TryGetRevisionAsync(LoreGlobalArgs global, CancellationToken cancellationToken)
	{
		string? hex = null;
		try
		{
			using LoreRevisionInfoArgs args = new();
			await Lore.RevisionInfo(global, args)
				.Callback((loreEvent, _) =>
				{
					if (loreEvent.Tag == LoreEventTag.REVISION_INFO && hex == null)
					{
						hex = Convert.ToHexString(loreEvent.GetData<LoreRevisionInfoEventDataFFI>().Revision.Data).ToLowerInvariant();
					}
				})
				.WaitAsync();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Unable to query current Lore revision");
		}
		return hex;
	}

	private void ForwardLogEvent(LoreEventFFI loreEvent, ulong userContext)
	{
		LoreLogEventDataFFI data = loreEvent.GetData<LoreLogEventDataFFI>();
		if (data.Level > LoreLogLevel.DEBUG)
		{
			_logger.LogInformation("{Message}", data.Message);
		}
	}

	private static void Check(string operation, int resultCode)
	{
		if (resultCode != 0)
		{
			throw new WorkspaceMaterializationException($"Lore {operation} failed (code {resultCode})");
		}
	}

	private async Task SaveStateAsync(State state, CancellationToken cancellationToken)
	{
		try
		{
			Directory.CreateDirectory(_metadataDir.FullName);
			string json = JsonSerializer.Serialize(state, s_jsonOptions);
			await File.WriteAllTextAsync(_stateTempFile, json, cancellationToken);
			File.Move(_stateTempFile, _stateFile, overwrite: true);
		}
		catch (Exception e)
		{
			throw new WorkspaceMaterializationException("Failed to save local workspace state", e);
		}
	}

	private async Task<State?> LoadStateAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (!File.Exists(_stateFile))
			{
				return null;
			}
			string json = await File.ReadAllTextAsync(_stateFile, cancellationToken);
			return JsonSerializer.Deserialize<State>(json, s_jsonOptions);
		}
		catch (Exception e)
		{
			_logger.LogWarning(e, "Failed to load local workspace state");
			return null;
		}
	}
}

/// <summary>
/// Factory for <see cref="LoreMaterializer" />
/// </summary>
public class LoreMaterializerFactory(IServiceProvider serviceProvider) : IWorkspaceMaterializerFactory
{
	/// <inheritdoc/>
	public Task<IWorkspaceMaterializer?> CreateMaterializerAsync(string name, RpcAgentWorkspace workspaceInfo, DirectoryReference workspaceDir, bool forAutoSdk, CancellationToken cancellationToken)
	{
		if (name.Equals(LoreMaterializer.TypeName, StringComparison.OrdinalIgnoreCase))
		{
			ILogger<LoreMaterializer> logger = serviceProvider.GetRequiredService<ILogger<LoreMaterializer>>();
			Tracer tracer = serviceProvider.GetRequiredService<Tracer>();
			LoreMaterializerOptions options = LoreMaterializerOptions.FromMethodString(workspaceDir.FullName, workspaceInfo);
			return Task.FromResult<IWorkspaceMaterializer?>(new LoreMaterializer(options, tracer, logger));
		}
		return Task.FromResult<IWorkspaceMaterializer?>(null);
	}
}
