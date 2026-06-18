// Copyright Epic Games, Inc. All Rights Reserved.

using HordeServer.Jobs;
using HordeServer.Plugins;
using HordeServer.VersionControl;
using HordeServer.VersionControl.Lore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HordeServer
{
	/// <summary>
	/// Entry point for the Lore plugin.
	/// </summary>
	[Plugin("Lore", EnabledByDefault = false, GlobalConfigType = typeof(LoreConfig), DependsOn = new[] { "Build" })]
	public class LorePlugin : IPluginStartup
	{
		/// <inheritdoc/>
		public void Configure(IApplicationBuilder app)
		{
		}

		/// <inheritdoc/>
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddSingleton<LoreService>();
			services.AddSingleton<IVersionControlService>(sp => sp.GetRequiredService<LoreService>());
			services.AddSingleton<IWorkspaceMessageEnricher, LoreWorkspaceMessageEnricher>();
		}
	}
}
