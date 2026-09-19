namespace LlmChamber.Speech;

/// <summary>音声機能（STT/TTS）の初期化オプション。</summary>
public sealed class SpeechOptions
{
    /// <summary>
    /// Whisperモデルサイズ。デフォルトはBase（精度と速度のバランス）。
    /// </summary>
    public WhisperModelSize WhisperModel { get; set; } = WhisperModelSize.Base;

    /// <summary>
    /// Whisper バイナリ（whisper-cli）への明示的パス。
    /// nullの場合、自動DLでキャッシュディレクトリに展開する。
    /// </summary>
    public string? WhisperBinaryPath { get; set; }

    /// <summary>
    /// Whisper ggml モデルファイル（.bin）への明示的パス。
    /// nullの場合、<see cref="WhisperModel"/> に応じて自動DLする。
    /// </summary>
    public string? WhisperModelPath { get; set; }

    /// <summary>
    /// Piper バイナリへの明示的パス。
    /// nullの場合、自動DLでキャッシュディレクトリに展開する。
    /// </summary>
    public string? PiperBinaryPath { get; set; }

    /// <summary>
    /// デフォルトの Piper voice 名（例: "en_US-amy-medium"）。
    /// <see cref="SpeakOptions.Voice"/> が指定されない場合に使用される。
    /// </summary>
    public string DefaultVoice { get; set; } = "en_US-amy-medium";

    /// <summary>
    /// Piper voice の格納ディレクトリ。
    /// nullの場合、キャッシュディレクトリ配下に自動配置する。
    /// </summary>
    public string? PiperVoicesDirectory { get; set; }

    /// <summary>
    /// バイナリ・モデル・voice の自動ダウンロードを有効にするかどうか。
    /// false の場合は明示パスまたは有効なキャッシュだけを使用する。
    /// </summary>
    public bool AutoDownload { get; set; } = true;
}
