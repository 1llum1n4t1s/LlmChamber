namespace LlmChamber;

/// <summary>LlmChamberの基底例外。</summary>
public class LlmChamberException : Exception
{
    /// <summary>新しいインスタンスを初期化する。</summary>
    public LlmChamberException(string message) : base(message) { }

    /// <summary>内部例外付きで新しいインスタンスを初期化する。</summary>
    public LlmChamberException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Ollamaランタイムが見つからない場合の例外。</summary>
public class RuntimeNotFoundException : LlmChamberException
{
    /// <summary>ダウンロードを試みたURL。</summary>
    public string? DownloadUrl { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public RuntimeNotFoundException(string message, string? downloadUrl = null)
        : base(message) => DownloadUrl = downloadUrl;

    /// <summary>内部例外付きで新しいインスタンスを初期化する。</summary>
    public RuntimeNotFoundException(string message, string? downloadUrl, Exception innerException)
        : base(message, innerException) => DownloadUrl = downloadUrl;
}

/// <summary>Ollamaプロセスの起動に失敗した場合の例外。</summary>
public class ProcessStartException : LlmChamberException
{
    /// <summary>標準エラー出力の内容。</summary>
    public string? StandardError { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public ProcessStartException(string message, string? standardError = null)
        : base(message) => StandardError = standardError;

    /// <summary>内部例外付きで新しいインスタンスを初期化する。</summary>
    public ProcessStartException(string message, string? standardError, Exception innerException)
        : base(message, innerException) => StandardError = standardError;
}

/// <summary>Ollama APIの呼び出しに失敗した場合の例外。</summary>
public class OllamaApiException : LlmChamberException
{
    /// <summary>HTTPステータスコード。</summary>
    public int? StatusCode { get; }

    /// <summary>レスポンスボディ。</summary>
    public string? ResponseBody { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public OllamaApiException(string message, int? statusCode = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>内部例外付きで新しいインスタンスを初期化する。</summary>
    public OllamaApiException(string message, int? statusCode, string? responseBody, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>モデルが見つからない場合の例外。</summary>
public class ModelNotFoundException : LlmChamberException
{
    /// <summary>見つからなかったモデルのタグ。</summary>
    public string ModelTag { get; }

    /// <summary>新しいインスタンスを初期化する。</summary>
    public ModelNotFoundException(string modelTag)
        : base($"モデル '{modelTag}' が見つかりません。") => ModelTag = modelTag;
}
