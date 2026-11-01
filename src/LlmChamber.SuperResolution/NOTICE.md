# 超解像モデルと推論ランタイム

このモジュールはモデルをパッケージに同梱せず、選択したモデルを初回処理時に以下の配布元から取得します。取得先のrevision、ファイルサイズ、SHA-256は `ModelCache.cs` に固定しています。

| 対象 | 配布元・ライセンス表記 |
| --- | --- |
| Real-ESRGAN x4plus ONNX | [anakhiu/realesrgan-onnx](https://huggingface.co/anakhiu/realesrgan-onnx)。配布カードはBSD-3-Clause、公式RealESRGAN_x4plus.pthからの変換と記載 |
| Real-HAT-GAN FP32 ONNX | [HGhog/DeepSR](https://huggingface.co/HGhog/DeepSR)。配布カードはApache-2.0。配布物名はreal_hat_gan_srx4_fp32.onnxで、詳細な変換手順は同カードに記載されていません |
| Real-HAT-GAN Sharper FP32 ONNX | [HAT公式配布フォルダー](https://drive.google.com/drive/folders/1HpmReFfoUqUbnAOQ7rvOeNU3uf_m69w0) の `Real_HAT_GAN_sharper.pth`（file ID: `1EioFq5-mKmv1uqta_Byd9cgXp9SU3zjj`、Apache-2.0）のEMA重みから変換。Kagayoiの配信先を使用 |
| ONNX Runtime / DirectML | [Microsoft ONNX Runtime](https://github.com/microsoft/onnxruntime)。使用パッケージはMicrosoft.ML.OnnxRuntime.DirectML 1.24.4。各同梱コンポーネントの条件はNuGetパッケージのLICENSE、ThirdPartyNotices等に従います |

原著プロジェクトは [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN)（BSD-3-Clause）と [HAT](https://github.com/XPixelGroup/HAT)（Apache-2.0）です。上記のONNX配布物は各原著プロジェクト自身の公式ONNXリリースではありません。

Sharper の公式チェックポイントは170277017 bytes、SHA-256 `5800b67136006eb8cab3b4ed7c8d73b6a195bb18e6cc709b674f9aa069c00271` です。[固定revisionのミラー](https://huggingface.co/anchuang/Real_HAT_GAN_SRx4_sharper/tree/4d7eb31975e6a8f26f3d2c474ebc1515924149c6) のファイルも公式配布物とのハッシュ一致を確認しています。

再変換にはPython 3.12、torch 2.15.0、spandrel 0.4.2、onnx 1.23.2を使い、`python tools/export_hat_sharper.py <checkpoint.pth> <output.onnx>` を実行します。FP32、opset 17、batch 1、動的な縦横寸法（16の倍数）、外部データなしで変換し、追加のシャープ処理は行いません。Pythonと変換用依存は開発時だけ必要で、利用者の実行環境には不要です。
