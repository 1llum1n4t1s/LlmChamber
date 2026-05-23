namespace LlmChamber.Media;

/// <summary>動画機能の基底例外。</summary>
public class MediaException : LlmChamberException
{
    /// <summary>新しいインスタンスを初期化する。</summary>
    public MediaException(string message) : base(message) { }

    /// <summary>内部例外付きで新しいインスタンスを初期化する。</summary>
    public MediaException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>FFmpeg バイナリが見つからない場合の例外。</summary>
public class FFmpegBinaryNotFoundException : MediaException
{
    /// <summary>新しいインスタンスを初期化する。</summary>
    public FFmpegBinaryNotFoundException(string message) : base(message) { }
}

/// <summary>FFmpeg プロセス実行が失敗した場合の例外。</summary>
public class FFmpegRuntimeException : MediaException
{
    /// <summary>プロセスのexitコード。</summary>
    public int? ExitCode { get; }

    /// <summary>標準エラー出力。</summary>
    public string? StandardError { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public FFmpegRuntimeException(string message, int? exitCode = null, string? standardError = null)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }
}
