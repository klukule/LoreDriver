// Copyright Epic Games, Inc. All Rights Reserved.

using EpicGames.Horde.Jobs;
using HordeCommon.Rpc.Messages;
using HordeServer.Agents;
using HordeServer.Jobs;
using HordeServer.Streams;
using Microsoft.Extensions.Options;

namespace HordeServer.VersionControl.Lore
{
	/// <summary>
	/// Rewrites the base agent workspace message into a Lore workspace for streams using Lore as a VCS.
	/// </summary>
	public sealed class LoreWorkspaceMessageEnricher : IWorkspaceMessageEnricher
	{
		readonly IOptionsMonitor<BuildConfig> _buildConfig;
		readonly IOptionsMonitor<LoreConfig> _loreConfig;
		readonly LoreService _loreService;

		/// <summary>
		/// Constructor
		/// </summary>
		public LoreWorkspaceMessageEnricher(IOptionsMonitor<BuildConfig> buildConfig, IOptionsMonitor<LoreConfig> loreConfig, LoreService loreService)
		{
			_buildConfig = buildConfig;
			_loreConfig = loreConfig;
			_loreService = loreService;
		}

		/// <inheritdoc/>
		public async Task EnrichAsync(RpcAgentWorkspace workspace, AgentWorkspaceInfo workspaceInfo, IAgent agent, IJob job, CancellationToken cancellationToken)
		{
			if (!_buildConfig.CurrentValue.TryGetStream(job.StreamId, out StreamConfig? streamConfig) || !String.Equals(streamConfig.VCS, "Lore", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			LoreClusterConfig? cluster = _loreConfig.CurrentValue.FindClusterForStream(streamConfig);
			if (cluster == null)
			{
				return;
			}

			string branch = String.IsNullOrEmpty(streamConfig.DefaultBranchName) ? "main" : streamConfig.DefaultBranchName;

			workspace.Cluster = cluster.Name;
			workspace.ServerAndPort = cluster.ServerAndPort;
			workspace.BaseServerAndPort = cluster.ServerAndPort;
			workspace.Stream = streamConfig.RepositoryName ?? streamConfig.Name;
			workspace.Method = $"name=lore&branch={branch}";

			LoreCredentials? credentials = cluster.Credentials.FirstOrDefault();
			workspace.UserName = credentials?.UserName;
			workspace.Password = credentials?.Password;
			workspace.Ticket = credentials?.Ticket;

			// We need both the numeric ID and the hash since Lore actually syncs based on the hash, but numeric IDs are used for other things in Horde. (so resolve ID to hash)
			string? revisionHash = await _loreService.TryGetRevisionHashAsync(streamConfig, job.CommitId, cancellationToken);
			if (!String.IsNullOrEmpty(revisionHash))
			{
				workspace.RevisionHash = revisionHash;
			}
		}
	}
}
