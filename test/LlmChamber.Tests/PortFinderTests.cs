using LlmChamber.Internal;
using Xunit;

namespace LlmChamber.Tests;

public class PortFinderTests
{
    [Fact]
    public void FindAvailablePort_ReturnsValidPort()
    {
        int port = PortFinder.FindAvailablePort();
        Assert.InRange(port, 1024, 65535);
    }

}
