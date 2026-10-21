using RustServer.Plugins.Features.PluginManagement.Models;

namespace RustServer.Plugins.Features.PluginManagement.Commands.SwitchEnvironment;

public sealed record SwitchEnvironmentCommand(PluginEnvironment Environment);
