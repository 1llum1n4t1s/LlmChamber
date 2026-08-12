# CLAUDE.md

このファイルは、このリポジトリで作業する Claude Code 向けのガイダンスを提供する。

## プロジェクト概要

LlmChamberは「NuGet一発、ゼロ設定、環境汚染なし」で.NETアプリにローカルLLMを組み込むライブラリ。Ollamaバイナリをアプリローカルに自動配置し、モデルの自動pull・プロセス管理・推論をC#の型安全なAPIで提供する。

## ビルド・テスト

```bash
# ソリューション全体ビルド
dotnet build LlmChamber.slnx

# ユニットテスト実行
dotnet test test/LlmChamber.Tests/LlmChamber.Tests.csproj --framework net10.0

# 特定テストクラス実行
dotnet test test/LlmChamber.Tests/LlmChamber.Tests.csproj --filter "FullyQualifiedName~OllamaModelsTests"

# NuGetパッケージ生成
dotnet pack LlmChamber.slnx -o artifacts
```

## ターゲットフレームワーク

net8.0 / net10.0 のマルチターゲット。WPF・WinFormsは `-windows` TFM。

## アーキテクチャ: CRDebugger方式（ソースインクルード）

**重要**: `LlmChamber.Core` は `IsPackable=false` の内部プロジェクト。NuGetには公開せず、内部ソースとして各公開パッケージへ取り込む。

各公開パッケージ（LlmChamber, .Wpf, .Avalonia, .WinForms, .Maui）が Core の `.cs` ファイルを `<Compile Include>` で直接取り込む。これにより NuGet に "Core" パッケージを出さない。

```xml
<!-- 各公開パッケージの.csprojに記載 -->
<Compile Include="..\LlmChamber.Core\**\*.cs"
         Link="Core\%(RecursiveDir)%(Filename)%(Extension)"
         Exclude="..\LlmChamber.Core\obj\**;..\LlmChamber.Core\bin\**" />
```

**結果**: コードは全て `src/LlmChamber.Core/` に書く。公開パッケージのプロジェクトにはUI固有コードのみ配置する。

## NuGetパッケージ構成（5種）

| PackageId | 用途 | TFM |
|---|---|---|
| `LlmChamber` | ヘッドレス（コンソール/WebAPI） | net8.0;net10.0 |
| `LlmChamber.Wpf` | WPFチャットコントロール | net8.0-windows;net10.0-windows |
| `LlmChamber.Avalonia` | Avaloniaチャットコントロール | net8.0;net10.0 |
| `LlmChamber.WinForms` | WinFormsチャットパネル | net8.0-windows;net10.0-windows |
| `LlmChamber.Maui` | MAUIチャットビュー | net8.0;net10.0 |

## Core内部の層構造

### 公開API（`LlmChamber` 名前空間）
- `ILocalLlm` — メインエントリポイント。IAsyncDisposable。`UseSpeech()` / `UseMedia()` でオプトイン拡張
- `IChatSession` — マルチターン対話。`SendAsync(message, images, ct)` で画像入力対応
- `IRuntimeManager` — Ollamaバイナリ・モデル管理
- `ChatMessage` — `Images` プロパティで multimodal モデル対応
- `LlmChamberFactory.Create()` — 非DI用ファクトリ
- `ServiceCollectionExtensions.AddLlmChamber()` — DI登録

### 公開API（`LlmChamber.Speech` 名前空間 — オプトイン）
- `ISpeechSession` — STT/TTS セッション。`TranscribeAsync` / `SpeakAsync`
- `SpeechOptions`, `TranscribeOptions`, `SpeakOptions`, `WhisperModelSize`
- `TranscriptionResult`, `TranscriptionSegment`
- `SpeechException`, `SpeechBinaryNotFoundException`, `SpeechModelNotFoundException`, `SpeechRuntimeException`

### 公開API（`LlmChamber.Media` 名前空間 — オプトイン）
- `IVideoSession` — 動画解析セッション。`AnalyzeAsync` / `ExtractFramesAsync`
- `MediaOptions`, `VideoAnalysisOptions`, `VideoFrameAnalysis`, `VideoFrameFormat`
- `MediaException`, `FFmpegBinaryNotFoundException`, `FFmpegRuntimeException`

### 内部実装（`LlmChamber.Internal` 名前空間）
- `LocalLlm` → `OllamaProcessManager` → `OllamaApiClient` の順で初期化を自動実行
- `OllamaDownloader` — GitHub Releasesからバイナリをアトミックにダウンロード
- `GpuDetector` — PowerShell CIM (Get-CimInstance) でGPU/NPU検出
- `NdjsonStreamReader` — Ollama NDJSON → IAsyncEnumerable変換
- `OllamaModels` — 組込みプリセット7種（テキスト4 + Vision3）
  - テキスト: gemma4-e2b, gemma4-e4b, qwen3.5-2b, phi4-mini
  - Vision: gemma3-4b, qwen2.5vl-3b, llava-7b

### Speech 内部実装（`LlmChamber.Internal.Speech` 名前空間）
- `SpeechSession` — `ISpeechSession` 実装。`SemaphoreSlim` で1回だけ初期化
- `WhisperBinaryDownloader` — whisper.cpp バイナリ (Windows x64 のみ自動DL)
- `WhisperModelDownloader` — HuggingFace から ggml モデル取得
- `WhisperRunner` — whisper-cli プロセス起動、JSON 出力パース
- `PiperBinaryDownloader` — rhasspy/piper 全プラットフォーム自動DL
- `PiperVoiceDownloader` — HuggingFace から `.onnx` + `.onnx.json` 取得
- `PiperRunner` — piper プロセス起動、stdin でテキスト送信

### Media 内部実装（`LlmChamber.Internal.Media` 名前空間）
- `VideoSession` — `IVideoSession` 実装。フレーム抽出 → Vision API デリゲートへ連投
- `FFmpegBinaryDownloader` — BtbN/FFmpeg-Builds latest 自動DL（Windows x64 / Linux x64・arm64）
- `FFmpegFrameExtractor` — ffmpeg プロセスでフレーム画像を一時ディレクトリに出力

### Ollama API DTO（`LlmChamber.Internal.Api` 名前空間）
- `OllamaJsonContext` — System.Text.Json Source Generator
- `GenerateRequest` / `OllamaMessage` に `Images` (Base64 List) プロパティ
- Request/Response型はOllama HTTP APIの各エンドポイントに対応

## 重要な設計判断

- **WPFプロジェクトでは `System.IO` 等の暗黙インポートが効かない**。Core内のコードには明示的 `using` を書く
- **DI登録はファクトリデリゲート + Keyed Services方式**。`ILogger<T>` の有無に依存しない（GetService + NullLoggerフォールバック）。HttpClientは `LlmChamberHttpClients.Downloader` / `.Api` のキーで2つ登録（BaseAddress競合回避）
- **Speech/Media 用 HttpClient は Downloader を再利用**。`LocalLlm` コンストラクタが `HttpClient` を受け取り、Speech/Media downloader へ流し込む
- **ChatSession.SendAsync失敗時はユーザーメッセージをロールバック**する（ゴーストコンテキスト防止）
- **HttpClient.Timeout = InfiniteTimeSpan**。非ストリーミング推論は30分CancellationTokenで制御
- **バージョンマーカー形式は `version:variant`**（例: `0.20.2:Full`）
- **ROCm版は2段階ダウンロード**（Windows/Linux）: Full版でollama本体を取得後、ROCm追加DLLを上書き展開。macOSではROCm非対応のためフォールバック
- **UnsupportedPlatformExceptionはPlatformNotSupportedException派生**。`catch (PlatformNotSupportedException)` で捕捉可能
- **Vision はコア標準・依存ゼロ**。Ollama multimodal API の `images` (Base64) フィールドだけで実現。`OllamaApiClient.EncodeImages(byte[][])` で変換
- **Speech/Media は遅延初期化でゼロコスト**。`UseSpeech()` / `UseMedia()` 呼び出し自体ではバイナリDLせず、セッション作成のみ行う。最初の `TranscribeAsync` / `SpeakAsync` / `AnalyzeAsync` 呼び出し時に `SemaphoreSlim` で1回だけ Whisper/Piper/FFmpeg バイナリ + モデルを取得する
- **Speech は外部バイナリ自動DL**。.NET NuGet バインディング（Whisper.net や Piper）ではなく、whisper.cpp / piper / ffmpeg の公式リリースバイナリをアプリローカル展開して使う（Ollama と同じ思想）
- **VideoSession.FrameAnalyzer デリゲート**で循環依存を回避: `LocalLlm.UseMedia` 内で Vision API (`GenerateCompleteAsync`) をラップしたデリゲートを `VideoSession` に注入

## Avalonia UI ルール

- AXAML構文を使う（WPF XAMLではなく AXAML で書く）
- 要素の出し分けは Style セレクターで行う（`Trigger` / `DataTrigger` は存在しないため使わない）
- 表示制御は `IsVisible=false` を使う（`Visibility.Collapsed` は存在しないため使わない）
- `StyledProperty<T>` パターンを使う（DependencyPropertyではなく StyledProperty で定義する）

## テスト

- xUnit + NSubstitute
- `InternalsVisibleTo` で Core 内部クラスもテスト可能
- インテグレーションテストは `[Trait("Category", "Integration")]` で分離
