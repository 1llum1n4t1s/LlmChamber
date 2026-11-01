using System.IO;
using System.Runtime.CompilerServices;

namespace LlmChamber.Internal;

/// <summary>
/// NDJSON（改行区切りJSON）ストリームリーダー。
/// Ollama APIのストリーミング応答を読み取る。
/// </summary>
internal static class NdjsonStreamReader
{
    /// <summary>StreamReaderから行単位で生のJSON文字列を返す。</summary>
    public static async IAsyncEnumerable<string> ReadLinesAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }
}
