namespace LlmChamber.Speech;

/// <summary>音声機能の基底例外。</summary>
public class SpeechException : LlmChamberException
{
    /// <summary>新しいインスタンスを初期化する。</summary>
    public SpeechException(string message) : base(message) { }

    /// <summary>内部例外付きで新しいインスタンスを初期化する。</summary>
    public SpeechException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Whisper/Piperバイナリが見つからない場合の例外。</summary>
public class SpeechBinaryNotFoundException : SpeechException
{
    /// <summary>探していたバイナリ名（whisper-cli / piper）。</summary>
    public string BinaryName { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public SpeechBinaryNotFoundException(string binaryName, string message)
        : base(message) => BinaryName = binaryName;
}

/// <summary>Whisper/Piperモデルが見つからない場合の例外。</summary>
public class SpeechModelNotFoundException : SpeechException
{
    /// <summary>探していたモデル名。</summary>
    public string ModelName { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public SpeechModelNotFoundException(string modelName, string message)
        : base(message) => ModelName = modelName;
}

/// <summary>Whisper/Piperプロセスの実行に失敗した場合の例外。</summary>
public class SpeechRuntimeException : SpeechException
{
    /// <summary>プロセスのexitコード。</summary>
    public int? ExitCode { get; }

    /// <summary>標準エラー出力。</summary>
    public string? StandardError { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public SpeechRuntimeException(string message, int? exitCode = null, string? standardError = null)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }
}
