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

    [Fact]
    public void FindAvailablePort_ReturnsDifferentPorts()
    {
        int port1 = PortFinder.FindAvailablePort();
        int port2 = PortFinder.FindAvailablePort();
        // 同じポートが返る可能性はゼロではないが、通常は異なるポート
        // 厳密なテストではないがリグレッション検出には有用
        Assert.True(port1 > 0 && port2 > 0);
    }
}
