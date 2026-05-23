namespace LlmChamber.Speech;

/// <summary>音声文字起こし（STT）の結果。</summary>
/// <param name="Text">文字起こし全文。</param>
/// <param name="Duration">音声の長さ。</param>
/// <param name="Segments">タイムスタンプ付きセグメント。</param>
/// <param name="DetectedLanguage">検出された言語コード（例: "ja", "en"）。</param>
public sealed record TranscriptionResult(
    string Text,
    TimeSpan? Duration,
    IReadOnlyList<TranscriptionSegment>? Segments,
    string? DetectedLanguage);

/// <summary>音声文字起こしの1セグメント。</summary>
/// <param name="Start">開始時刻。</param>
/// <param name="End">終了時刻。</param>
/// <param name="Text">セグメントのテキスト。</param>
public sealed record TranscriptionSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text);

/// <summary>STT実行時のオプション。</summary>
public sealed class TranscribeOptions
{
    /// <summary>
    /// 言語コード（"ja", "en", "auto" 等）。
    /// nullまたは"auto"の場合、Whisperが自動検出する。
    /// </summary>
    public string? Language { get; set; }

    /// <summary>翻訳モード。trueなら英訳を出力する。</summary>
    public bool TranslateToEnglish { get; set; }

    /// <summary>Whisper実行スレッド数。</summary>
    public int Threads { get; set; } = 4;

    /// <summary>セグメント情報を含めるか。falseだと text のみで segments=null。</summary>
    public bool IncludeSegments { get; set; } = true;
}

/// <summary>TTS実行時のオプション。</summary>
public sealed class SpeakOptions
{
    /// <summary>
    /// 使用する Piper voice。例: "en_US-amy-medium", "ja_JP-takumi-medium"。
    /// nullの場合、<see cref="SpeechOptions.DefaultVoice"/> を使用する。
    /// </summary>
    public string? Voice { get; set; }

    /// <summary>
    /// 発話速度倍率（0.5～2.0）。1.0が標準。Piperの length_scale の逆数として作用。
    /// </summary>
    public float SpeakingRate { get; set; } = 1.0f;

    /// <summary>音声の高さスケール（0.5～2.0）。1.0が標準。</summary>
    public float NoiseScale { get; set; } = 0.667f;
}

/// <summary>Whisper モデルサイズ。</summary>
public enum WhisperModelSize
{
    /// <summary>tiny - 39M params, 75MB, 最速だが精度低</summary>
    Tiny,
    /// <summary>base - 74M params, 142MB, バランス型</summary>
    Base,
    /// <summary>small - 244M params, 466MB, 推奨</summary>
    Small,
    /// <summary>medium - 769M params, 1.5GB</summary>
    Medium,
    /// <summary>large-v3 - 1550M params, 3GB, 最高精度</summary>
    Large,
}
