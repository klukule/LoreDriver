// Copyright Epic Games, Inc. All Rights Reserved.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using EpicGames.Horde.Streams;
using HordeServer.Plugins;

namespace HordeServer
{
	/// <summary>
	/// Global configuration for the Lore plugin
	/// </summary>
	public class LoreConfig : IPluginConfig
	{
		/// <summary>
		/// Lore server clusters available to streams
		/// </summary>
		public List<LoreClusterConfig> Clusters { get; set; } = new List<LoreClusterConfig>();

		/// <summary>
		/// Per-stream Lore settings (which cluster a stream uses, optional branch override)
		/// </summary>
		public List<LoreStreamConfig> Streams { get; set; } = new List<LoreStreamConfig>();

		/// <inheritdoc/>
		public void PostLoad(PluginConfigOptions configOptions)
		{
		}

		/// <summary>
		/// Finds a cluster by name (or the first cluster if name is null)
		/// </summary>
		public LoreClusterConfig? FindCluster(string? name)
		{
			if (Clusters.Count == 0)
			{
				return null;
			}
			if (name == null)
			{
				return Clusters.FirstOrDefault();
			}
			return Clusters.FirstOrDefault(x => String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Finds the per-stream settings for a stream
		/// </summary>
		public LoreStreamConfig? FindStream(StreamId streamId) => Streams.FirstOrDefault(x => x.Id == streamId);

		/// <summary>
		/// Finds the cluster a stream is configured to use
		/// </summary>
		public LoreClusterConfig? FindClusterForStream(StreamId streamId) => FindCluster(FindStream(streamId)?.Cluster);
	}

	/// <summary>
	/// Per-stream Lore settings
	/// </summary>
	public class LoreStreamConfig
	{
		/// <summary>
		/// Stream this applies to
		/// </summary>
		[Required]
		public StreamId Id { get; set; }

		/// <summary>
		/// Lore cluster the stream uses
		/// </summary>
		public string? Cluster { get; set; }

		/// <summary>
		/// Optional branch override (defaults to the stream's DefaultBranchName, or "main")
		/// </summary>
		public string? Branch { get; set; }
	}

	/// <summary>
	/// A cluster of Lore servers
	/// </summary>
	[DebuggerDisplay("{Name}")]
	public class LoreClusterConfig
	{
		/// <summary>
		/// Name of the cluster
		/// </summary>
		[Required]
		public string Name { get; set; } = null!;

		/// <summary>
		/// Address of the Lore server (eg. lore://host:port)
		/// </summary>
		public string ServerAndPort { get; set; } = String.Empty;

		/// <summary>
		/// Server credentials
		/// </summary>
		public List<LoreCredentials> Credentials { get; set; } = new List<LoreCredentials>();
	}

	/// <summary>
	/// Credentials for a Lore user
	/// </summary>
	public class LoreCredentials
	{
		/// <summary>
		/// The username
		/// </summary>
		public string UserName { get; set; } = String.Empty;

		/// <summary>
		/// Password for the user
		/// </summary>
		public string? Password { get; set; }

		/// <summary>
		/// Login ticket for the user (used instead of password if set)
		/// </summary>
		public string? Ticket { get; set; }
	}
}
