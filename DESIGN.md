# DESIGN.md

## 目的と構成

LlmChamber は .NET アプリからローカル LLM を利用するライブラリ。Ollama の取得、専用プロセスの起動、モデル pull、HTTP 推論を管理し、テキスト・画像・音声・動画を C# API で扱う。利用方法は [README.md](README.md)、変更・検証の規約は [AGENTS.md](AGENTS.md) を参照する。

| 構成 | 責務と境界 |
| --- | --- |
| `src/LlmChamber.Core/` | 公開 API、Ollama 管理、Speech/Media の共通実装。内部ビルド・テスト用で `IsPackable=false` |
| `src/LlmChamber/` | ヘッドレス向け公開パッケージ |
| `src/LlmChamber.Wpf/`、`src/LlmChamber.WinForms/` | Windows UI。`net8.0-windows` / `net10.0-windows` |
| `src/LlmChamber.Avalonia/`、`src/LlmChamber.Maui/` | 各 UI フレームワークのコントロール |
| `samples/`、`test/` | 各 UI とコンソールの利用例、Core のユニットテストと統合テスト用プロジェクト |

Windows UI 以外のライブラリは `net8.0` / `net10.0`。5 公開パッケージは Core の `.cs` を `Compile Include` で直接取り込み、Core パッケージへの依存を公開しない。各 UI パッケージだけで共通 API も利用できる一方、共通コードの変更はすべての公開プロジェクトのコンパイルに影響する。

## 主要コンポーネントとデータフロー

1. `LlmChamberFactory.Create()` または `ServiceCollectionExtensions.AddLlmChamber()` が `ILocalLlm` の実装 `src/LlmChamber.Core/Internal/LocalLlm.cs` と管理コンポーネントを組み立てる。
2. 明示的な `InitializeAsync()` または最初の推論・チャット送信で、`RuntimeManager` がランタイムを確保する。`OllamaDownloader` は `PlatformInfo` と `GpuDetector` に従って GitHub Releases のアセットを選択する。
3. `OllamaProcessManager` が空きポートで `ollama serve` を起動し、ヘルスチェック後に `OllamaApiClient` の接続先を設定する。自動 pull が有効なら既定モデルを確認・取得する。
4. `OllamaApiClient` が generate / chat / embed とモデル管理 API を呼ぶ。`src/LlmChamber.Core/Internal/Api/OllamaJsonContext.cs` の System.Text.Json source generator を使い、ストリーミングの NDJSON は `NdjsonStreamReader` が非同期列挙へ変換する。画像は Base64 の `images` フィールドで渡すため、Vision 用の別ランタイムは不要。
5. `ChatSession` はシステムプロンプトと会話履歴を管理する。モデルのプリセット ID と Ollama タグの解決、既定推論パラメーターは `OllamaModels` が担当する。プリセットの一覧・数値の正本は同クラス。

`IRuntimeManager` はランタイム確保、モデル一覧・取得・削除、キャッシュ容量とバージョンを提供する。モデル操作は必要に応じてプロセスを起動するため、`LocalLlm.InitializeAsync()` とは独立して利用できる。

## リソースとプロセスの境界

既定キャッシュはユーザープロファイル配下の `.llmchamber`。`CacheDirectory` で変更できる。グローバルへのインストールは行わないが、既定キャッシュは実行ファイル隣接でもアプリごとに一意でもない。

| 保存先・接続 | 内容 |
| --- | --- |
| `CacheDirectory/runtime`、直下の `.version` | Ollama バイナリと `version:variant` マーカー |
| `CacheDirectory/models` | 子プロセスの `OLLAMA_MODELS`。`SharedModelDirectory` 指定時はその場所を使用 |
| `CacheDirectory/speech` | Whisper/Piper バイナリ、モデル、音声リソース。明示パスでの差し替えも可能 |
| `CacheDirectory/media` | FFmpeg バイナリ |
| OS 一時ディレクトリ | 音声入出力や抽出フレーム。処理の終了時に削除を試みる |
| `localhost` の空きポート | 専用 Ollama サーバー。`OLLAMA_HOST` は子プロセスの環境変数として設定 |

ダウンローダーは一意な一時アーカイブと展開先を使い、配備後にマーカーを記録する。Ollama の配備はファイル単位のコピー・上書きであり、ディレクトリ全体のトランザクションではない。Windows/Linux の ROCm は Full 本体を配備後、ROCm 追加アセットを重ねる。macOS は共通アセットを使用する。Linux の Ollama `.tar.zst` 展開は外部 `tar` コマンドを使用する。

GPU 判定は Windows の PowerShell CIM、Linux の `lspci`、macOS の `sysctl` を使用する。NPU 情報も検出するが、専用 NPU 推論経路はない。`CpuOnly` と `Full` は同じ通常アセットを選択し、CPU 専用の別配布物や GPU 無効化設定を意味しない。

## Speech と Media

`UseSpeech()` / `UseMedia()` はセッションだけを作り、バイナリを取得しない。同じ `LocalLlm` ではセッションを再利用し、最初の options が使われる。

- Speech は STT と TTS を別々の `SemaphoreSlim` で遅延初期化する。STT は whisper-cli と ggml モデルを確保し、音声ファイルを渡して JSON をパースする。TTS は Piper にテキストを標準入力で渡し、WAV を返す。音声モデルは HuggingFace から取得する。
- Media は初回の解析またはフレーム抽出で FFmpeg を確保する。`FFmpegFrameExtractor` が一時ディレクトリへの画像抽出を完了してから列挙し、`VideoSession` が各フレームを順番に解析する。`LocalLlm` が注入する `FrameAnalyzer` デリゲートを介して Vision API を呼び、Media 実装から `LocalLlm` への直接依存を避ける。抽出だけなら LLM 推論は不要。
- .NET バインディングの代わりに外部 CLI を使うため、追加機能を未使用なら関連バイナリ・モデル取得を避けられる。一方、利用可能な OS/アーキテクチャは外部アセットに制約される。

コードに定義された自動取得の対応は、Whisper が Windows x64、Piper が Windows x64 と Linux/macOS x64・arm64、FFmpeg が Windows x64 と Linux x64・arm64。Whisper/Piper は各プロジェクトの GitHub Releases、FFmpeg は BtbN/FFmpeg-Builds の latest を参照する。未対応環境では options にバイナリパスを指定する。MAUI パッケージがあること自体はモバイル OS のランタイム対応を保証しない。

## 重要な不変条件と境界

- ダウンロード用と Ollama API 用の `HttpClient` は別インスタンス。送信後に変更できない `BaseAddress` の競合を避ける。DI のキーは `LlmChamberHttpClients.Downloader` / `.Api` で、事前登録されたクライアントを `TryAddKeyedSingleton` により維持する。Speech/Media は Downloader を再利用する。
- Factory が生成した両 HttpClient は `LocalLlm` が所有し、破棄時に解放する。DI 経由のクライアントは `LocalLlm` で破棄しない。`LocalLlm` は作成した Speech/Media セッションと Ollama プロセスを停止・破棄する。
- 標準 HttpClient の Timeout は無制限とし、キャンセルを伝播する。非ストリーミング推論は API クライアントのリンクされた CancellationTokenSource で最大30分に制限する。
- 初期化とプロセス起動はそれぞれセマフォで制御する。初期化済みでもプロセスが停止していれば再起動する。起動失敗時や破棄時は管理対象プロセスの終了を試みる。
- チャットの失敗・キャンセル・ストリーム列挙の途中終了ではユーザーメッセージをロールバックし、完走時だけ応答を履歴へ確定する。`ClearHistory` と履歴上限処理ではシステムメッセージを保持する。履歴操作の lock は送信全体を直列化するものではない。
- `AutoDownloadRuntime=false` はキャッシュのバイナリ・版・バリアントを確認し、不一致なら例外とする。`AutoPullModel=false` は初期化時の自動取得を抑止するもので、明示的な `EnsureModelAsync` を禁止しない。
- `UnsupportedPlatformException` は `PlatformNotSupportedException` 派生。他のライブラリ例外は `LlmChamberException` 系。呼び出し元が OS 非対応を標準例外で捕捉できる。

これらの挙動は `test/LlmChamber.Tests/` の LazyInitialization、ChatSession、Vision、Speech、Media、OllamaDownloader、PlatformInfo、Adversarial 系テストなどで検証する。ログは SuperLightLogger の `LogManager` から取得し、DI に `ILogger<T>` の登録は要求しない。
