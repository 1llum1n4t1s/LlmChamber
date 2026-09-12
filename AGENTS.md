# AGENTS.md

このリポジトリで作業するエージェント向けの規約。システムの構造と設計判断は [DESIGN.md](DESIGN.md)、利用方法は [README.md](README.md) を参照する。

## 実装の配置と制約

- 共通 API と内部実装は `src/LlmChamber.Core/` に置き、各公開パッケージには UI 固有コードを置く。Core のソース取り込み方式を維持し、`bin` / `obj` を取り込まない。Core 自体は NuGet に公開しない。
- 共通コードは全公開パッケージでコンパイルされる。Core 単体の成功だけで検証を終えず、WPF・WinForms・Avalonia・MAUI への影響を確認する。Core で必要な `System.IO` などは明示的に using し、MAUI と衝突する `SpeechOptions` などはエイリアスで解決する。
- `Directory.Build.props` の C# 12、nullable、警告をエラーとする設定に従う。公開 API の XML ドキュメントを保つ。
- 共通依存を変更するときは Core とソースを取り込む各 `.csproj` の参照も照合する。ロギングには既存の `SuperLightLogger.LogManager` / `ILog` を使う。
- ランタイム管理、HTTP、チャット、Speech/Media を変更するときは [DESIGN.md の不変条件](DESIGN.md#重要な不変条件と境界) と対応テストを確認する。

## ビルドと検証

.NET 10 SDK を使用する。ソリューションには Windows UI と MAUI サンプルが含まれるため、全体ビルドには Windows および対象 MAUI workload が必要。公開パッケージの TFM は DESIGN.md を参照する。

```powershell
# ソリューション全体
dotnet build LlmChamber.slnx

# ユニットテスト（両 TFM）
dotnet test test/LlmChamber.Tests/LlmChamber.Tests.csproj --framework net8.0
dotnet test test/LlmChamber.Tests/LlmChamber.Tests.csproj --framework net10.0

# 対象を絞った確認
dotnet test test/LlmChamber.Tests/LlmChamber.Tests.csproj --framework net10.0 --filter "FullyQualifiedName~OllamaModelsTests"

# パッケージ生成の確認が必要な場合
dotnet pack LlmChamber.slnx -c Release -o artifacts
```

- コード変更では影響するテストを実行し、共通コードでは公開パッケージのビルドも確認する。全体ビルドが環境要件でできない場合は、対象 `.csproj` を個別にビルドして未検証範囲を明示する。
- テストは xUnit v3 と NSubstitute。`test/LlmChamber.Tests/` は Core を ProjectReference し、`InternalsVisibleTo` で内部型を検証する。遅延初期化、HTTP 応答、ダウンロード先選択は既存のハンドラーや fixture に沿って検証する。
- 実バイナリやネットワークを必要とするテストは `test/LlmChamber.IntegrationTests/` に置き、`[Trait("Category", "Integration")]` で区別する。現状このプロジェクトにテスト実装はないため、実行成功を実接続の検証と扱わない。
- 配布時のビルド・pack 対象は `.github/workflows/publish.yml` を正本とする。5 公開プロジェクトを Release でビルドし、それぞれ `--no-build` で pack する構成。
- 文書だけの変更は、記載したパス・コマンド・挙動をコードと設定に照合し、リンクと差分を検証する。

## Avalonia UI

- AXAML を使用する。要素の出し分けは Style セレクターと `IsVisible` を使い、WPF の `Trigger` / `DataTrigger` / `Visibility.Collapsed` を持ち込まない。
- プロパティは `StyledProperty<T>` で定義する。

## 文書の責務

- AGENTS.md は作業規約・検証手順、DESIGN.md は現在の構造・責務・設計判断を記載する。設計の説明を命令ファイルに複製しない。
- README.md はライブラリ利用者向けの導入・API 利用・設定・制約を扱う。実装詳細は DESIGN.md または `docs/` へ参照を置く。
