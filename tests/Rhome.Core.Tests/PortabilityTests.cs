using Rhome.Core;

namespace Rhome.Core.Tests;

/// <summary>
/// PLAN.md §4: the core references only PKHeX.Core and the standard .NET library, so it can follow
/// emulation to any platform. Adding a UI or platform package to Rhome.Core fails this test.
/// </summary>
public class PortabilityTests
{
    [Fact]
    public void Core_references_only_PKHeX_and_the_base_library()
    {
        var references = typeof(SaveDetector).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name != "PKHeX.Core" && name != "netstandard"
                           && !name.StartsWith("System", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(references);
    }
}
