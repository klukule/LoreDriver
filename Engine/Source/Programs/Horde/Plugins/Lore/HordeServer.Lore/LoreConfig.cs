// Copyright Epic Games, Inc. All Rights Reserved.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using HordeServer.Plugins;
using HordeServer.Streams;

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
		/// Finds the Lore cluster a stream uses.
		public LoreClusterConfig? FindClusterForStream(StreamConfig streamConfig) => FindCluster(streamConfig.ClusterName) ?? Clusters.FirstOrDefault();
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
