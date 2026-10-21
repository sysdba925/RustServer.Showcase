using RustServer.Plugins.Features.PluginManagement.Models;
using RustServer.Plugins.Features.PluginManagement.Models.Git;
using RustServer.Plugins.Features.PluginManagement.Runtime;
using BaselineModel = RustServer.Plugins.Features.PluginManagement.Models.Baseline;

namespace RustServer.Plugins.Features.PluginManagement.SourcePreview;

public sealed record SourcePreviewResult(
    PluginEnvironment Environment,
    Source? CurrentSource,
    CurrentSourceDiagnostic? CurrentSourceDiagnostic,
    BaselineModel? Baseline,
    Source TargetSource,
    GitCommitSha PluginsHead,
    GitCommitSha ConfigsHead,
    IReadOnlyList<RuntimeFileChange> Changes);
