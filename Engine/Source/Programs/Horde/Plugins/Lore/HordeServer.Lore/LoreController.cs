// Copyright Epic Games, Inc. All Rights Reserved.

using EpicGames.Horde.Streams;
using HordeServer.Streams;
using HordeServer.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HordeServer
{
	/// <summary>
	/// A Lore stream, with the build-stream details it maps to
	/// </summary>
	public class GetLoreStreamResponse
	{
		/// <summary>Stream id</summary>
		public string Id { get; set; } = String.Empty;
		/// <summary>Display name of the stream</summary>
		public string Name { get; set; } = String.Empty;
		/// <summary>Lore cluster the stream uses</summary>
		public string? Cluster { get; set; }
		/// <summary>Branch that is built</summary>
		public string Branch { get; set; } = String.Empty;
		/// <summary>Lore repository name</summary>
		public string? Repository { get; set; }
	}

	/// <summary>
	/// A configured Lore cluster
	/// </summary>
	public class GetLoreClusterResponse
	{
		/// <summary>Cluster name</summary>
		public string Name { get; set; } = String.Empty;
		/// <summary>Lore server address (eg. lore://host:port)</summary>
		public string ServerAndPort { get; set; } = String.Empty;
		/// <summary>Ids of the streams that sync from this cluster</summary>
		public List<string> Streams { get; set; } = new List<string>();
	}

	/// <summary>
	/// Lore plugin status: configured clusters and streams
	/// </summary>
	public class GetLoreStatusResponse
	{
		/// <summary>
		/// Configured Lore clusters
		/// </summary>
		public List<GetLoreClusterResponse> Clusters { get; set; } = new List<GetLoreClusterResponse>();

		/// <summary>
		/// Configured Lore streams
		/// </summary>
		public List<GetLoreStreamResponse> Streams { get; set; } = new List<GetLoreStreamResponse>();
	}

	/// <summary>
	/// Read-only endpoints surfacing Lore configuration to the dashboard
	/// </summary>
	[Authorize]
	[ApiController]
	public class LoreController : HordeControllerBase
	{
		readonly IOptionsSnapshot<LoreConfig> _loreConfig;
		readonly IOptionsSnapshot<BuildConfig> _buildConfig;

		/// <summary>
		/// Constructor
		/// </summary>
		public LoreController(IOptionsSnapshot<LoreConfig> loreConfig, IOptionsSnapshot<BuildConfig> buildConfig)
		{
			_loreConfig = loreConfig;
			_buildConfig = buildConfig;
		}

		/// <summary>
		/// Returns the configured Lore clusters and streams
		/// </summary>
		[HttpGet]
		[Route("/api/v1/lore/status")]
		public ActionResult<GetLoreStatusResponse> GetStatus()
		{
			LoreConfig lore = _loreConfig.Value;
			BuildConfig build = _buildConfig.Value;

			List<GetLoreStreamResponse> streams = new();
			foreach (LoreStreamConfig stream in lore.Streams)
			{
				build.TryGetStream(stream.Id, out StreamConfig? streamConfig);
				string branch = stream.Branch ?? streamConfig?.DefaultBranchName ?? "main";
				streams.Add(new GetLoreStreamResponse
				{
					Id = stream.Id.ToString(),
					Name = streamConfig?.Name ?? stream.Id.ToString(),
					Cluster = stream.Cluster,
					Branch = branch,
					Repository = streamConfig?.RepositoryName,
				});
			}

			List<GetLoreClusterResponse> clusters = lore.Clusters.Select(cluster => new GetLoreClusterResponse
			{
				Name = cluster.Name,
				ServerAndPort = cluster.ServerAndPort,
				Streams = streams.Where(x => String.Equals(x.Cluster, cluster.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList(),
			}).ToList();

			return new GetLoreStatusResponse { Clusters = clusters, Streams = streams };
		}
	}
}
