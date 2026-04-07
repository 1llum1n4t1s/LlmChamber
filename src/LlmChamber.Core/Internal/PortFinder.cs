using System.Net;
using System.Net.Sockets;

namespace LlmChamber.Internal;

/// <summary>空きポートの検出。</summary>
internal static class PortFinder
{
    private const int MaxRetries = 3;

    /// <summary>
    /// 利用可能なTCPポートを取得する。
    /// OSにポート0でバインドさせて空きポートを確保する。
    /// TOCTOU軽減のため、ポートが本当に使えるか再確認リトライあり。
    /// </summary>
    public static int FindAvailablePort()
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            int port = AllocatePort();

            // 確認: ポートがまだ空いているか再バインドで検証
            try
            {
                using var verify = new TcpListener(IPAddress.Loopback, port);
                verify.Start();
                verify.Stop();
                return port;
            }
            catch (SocketException)
            {
                // 別プロセスに取られた → リトライ
            }
        }

        // 最終手段: 検証なしで返す
        return AllocatePort();
    }

    private static int AllocatePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
