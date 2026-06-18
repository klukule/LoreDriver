// Copyright Epic Games, Inc. All Rights Reserved.

using EpicGames.Core;
using JobDriver;
using JobDriver.Execution;
using JobDriver.Utility;
using LoreDriver;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

// Extension of the base JobDriver to add Lore materializer support. This is the entry point for the LoreDriver.

string? taskArg = Environment.GetEnvironmentVariable("UE_HORDE_JOB_DRIVER_TASK_ARG");
if (!String.IsNullOrEmpty(taskArg))
{
	Array.Resize(ref args, args.Length + 1);
	args[^1] = taskArg;
	Environment.SetEnvironmentVariable("UE_HORDE_JOB_DRIVER_TASK_ARG", null);
}

CommandLineArguments arguments = new CommandLineArguments(args);

IServiceCollection services = new ServiceCollection();
DriverApp.RegisterServices(services);
services.AddSingleton<IWorkspaceMaterializerFactory, LoreMaterializerFactory>();

await using ServiceProvider serviceProvider = services.BuildServiceProvider();
int exitCode = await CommandHost.RunAsync(arguments, serviceProvider, null);

TracerProvider? tracerProvider = serviceProvider.GetService<TracerProvider>();
LoggerProvider? loggerProvider = serviceProvider.GetService<LoggerProvider>();
JobStepLoggerFactory? stepLoggerFactory = serviceProvider.GetService<JobStepLoggerFactory>();

await Task.WhenAll(
	Task.Run(() => tracerProvider?.ForceFlush(5000)),
	Task.Run(() => loggerProvider?.ForceFlush(5000)),
	Task.Run(() => stepLoggerFactory?.OtelLoggerProvider?.ForceFlush(5000))
);

return exitCode;
