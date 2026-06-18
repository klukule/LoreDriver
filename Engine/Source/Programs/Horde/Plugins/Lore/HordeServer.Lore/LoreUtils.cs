// Copyright Epic Games, Inc. All Rights Reserved.

using HordeServer.Streams;

namespace HordeServer
{
	/// <summary>
	/// Shared constants and helpers for the Lore plugin.
	/// </summary>
	static class LoreUtils
	{
		/// <summary>
		/// VCS identifier a stream sets (StreamConfig.VCS) to use Lore.
		/// </summary>
		public const string VcsName = "Lore";

		/// <summary>
		/// Default branch name when none is explicitly provided.
		/// </summary>
		public const string DefaultBranch = "main";

		/// <summary>
		/// Materializer name written into the workspace method string (name=lore).
		/// </summary>
		public const string MaterializerName = "lore";

		/// <summary>
		/// True if the stream uses Lore as its version control provider.
		/// </summary>
		public static bool IsLoreStream(StreamConfig streamConfig) => String.Equals(streamConfig.VCS, VcsName, StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Gets the branch name for given stream, falling back to <see cref="DefaultBranch"/>.
		/// </summary>
		public static string GetBranch(StreamConfig streamConfig) => String.IsNullOrEmpty(streamConfig.DefaultBranchName) ? DefaultBranch : streamConfig.DefaultBranchName;

		/// <summary>
		/// Workspace method string that selects the Lore materializer for a branch.
		/// </summary>
		public static string GetWorkspaceMethod(string branch) => $"name={MaterializerName}&branch={branch}";
	}
}
