namespace Application.Tests;

// Application layer is empty in Phase A; real tests arrive with Phase B (auth) onwards.
public class PlaceholderTests
{
    [Fact]
    public void Assembly_References_Application_And_Domain()
    {
        Assert.NotNull(typeof(Domain.AssemblyMarker).Assembly);
        Assert.NotNull(typeof(Application.AssemblyMarker).Assembly);
    }
}
