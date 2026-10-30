# 超解像モデルと推論ランタイム

このモジュールはモデルをパッケージに同梱せず、選択したモデルを初回処理時に以下の配布元から取得します。取得先のrevision、ファイルサイズ、SHA-256は `ModelCache.cs` に固定しています。

| 対象 | 配布元・ライセンス表記 |
| --- | --- |
| Real-ESRGAN x4plus ONNX | [anakhiu/realesrgan-onnx](https://huggingface.co/anakhiu/realesrgan-onnx)。配布カードはBSD-3-Clause、公式RealESRGAN_x4plus.pthからの変換と記載 |
| Real-HAT-GAN FP32 ONNX | [HGhog/DeepSR](https://huggingface.co/HGhog/DeepSR)。配布カードはApache-2.0。配布物名はreal_hat_gan_srx4_fp32.onnxで、詳細な変換手順は同カードに記載されていません |
| ONNX Runtime / DirectML | [Microsoft ONNX Runtime](https://github.com/microsoft/onnxruntime)。使用パッケージはMicrosoft.ML.OnnxRuntime.DirectML 1.24.4。各同梱コンポーネントの条件はNuGetパッケージのLICENSE、ThirdPartyNotices等に従います |

原著プロジェクトは [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN)（BSD-3-Clause）と [HAT](https://github.com/XPixelGroup/HAT)（Apache-2.0）です。上記のONNX配布物は各原著プロジェクト自身の公式ONNXリリースではありません。
