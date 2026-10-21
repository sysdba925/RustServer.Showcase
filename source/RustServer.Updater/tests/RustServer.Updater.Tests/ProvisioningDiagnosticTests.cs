using RustServer.Updater.Executor;

namespace RustServer.Updater.Tests;

public sealed class ProvisioningDiagnosticTests
{
    [Fact]
    public void ReadsOnlyBoundedStructuredDiagnostic()
    {
        using var diagnostic = new ProvisioningDiagnosticFile();
        File.WriteAllText(diagnostic.Path, "{\"version\":1,\"stage\":\"database\",\"category\":\"external_command\",\"reason\":\"postgresql_command_failed\"}");
        Assert.Equal("stage database; category external_command; reason postgresql_command_failed;", diagnostic.Read());
        File.WriteAllText(diagnostic.Path, "{\"version\":1,\"stage\":\"database\",\"category\":\"external_command\",\"reason\":\"password=secret\"}");
        Assert.Contains("stage unknown; category unknown", diagnostic.Read());
        File.WriteAllText(diagnostic.Path, new string('x', 1025));
        Assert.Contains("stage unknown; category unknown", diagnostic.Read());
    }
}
