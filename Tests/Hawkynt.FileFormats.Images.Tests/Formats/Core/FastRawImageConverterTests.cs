using System;
using System.Linq;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class FastRawImageConverterTests {

  private static readonly PixelFormat[] _YuvFormats = Enum.GetValues<PixelFormat>()
    .Where(RawImage.IsPlanarYuvFormat)
    .ToArray();

  [Test]
  [Category("Unit")]
  public void RgbCanBeConvertedToEveryPlanarYuvLayout() {
    const int width = 7;
    const int height = 5;
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 31 + y * 7);
      rgb[at + 1] = (byte)(x * 11 + y * 37);
      rgb[at + 2] = (byte)(255 - x * 19 - y * 13);
    }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };

    foreach (var format in _YuvFormats) {
      var yuv = FastRawImageConverter.Convert(source, format, RawImageColorInfo.Bt709Limited);

      Assert.Multiple(() => {
        Assert.That(yuv.Format, Is.EqualTo(format), format.ToString());
        Assert.That(yuv.PixelData.LongLength, Is.EqualTo(yuv.MinimumPixelDataLength), format.ToString());
        Assert.That(yuv.HasEnoughPixelData, Is.True, format.ToString());
        Assert.That(yuv.ColorInfo?.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt709), format.ToString());
        Assert.That(yuv.ColorInfo?.Range, Is.EqualTo(RawColorRange.Limited), format.ToString());
      });

      var restored = FastRawImageConverter.Convert(yuv, PixelFormat.Rgb24);
      Assert.That(restored.PixelData.Length, Is.EqualTo(rgb.Length), format.ToString());
    }
  }

  [Test]
  [Category("Unit")]
  public void Bt601LimitedBlackAndWhiteUseStudioRangeCodes() {
    var source = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0, 0, 0, 255, 255, 255],
    };

    var yuv = FastRawImageConverter.Convert(source, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);

    Assert.That(yuv.GetPlaneData(0).ToArray(), Is.EqualTo(new byte[] { 16, 235 }));
    Assert.That(yuv.GetPlaneData(1).ToArray(), Is.EqualTo(new byte[] { 128, 128 }));
    Assert.That(yuv.GetPlaneData(2).ToArray(), Is.EqualTo(new byte[] { 128, 128 }));

    var restored = FastRawImageConverter.Convert(yuv, PixelFormat.Rgb24);
    Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FullRangeNeutralRampRoundTripsExactlyAtEndpoints() {
    var full709 = new RawImageColorInfo {
      Range = RawColorRange.Full,
      Primaries = RawColorPrimaries.Bt709,
      Transfer = RawTransferCharacteristic.Srgb,
      Matrix = RawMatrixCoefficients.Bt709,
      ChromaLocation = RawChromaLocation.Center,
    };
    var source = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [
        0, 0, 0, 255,
        64, 64, 64, 255,
        192, 192, 192, 255,
        255, 255, 255, 255,
      ],
    };

    var yuv = FastRawImageConverter.Convert(source, PixelFormat.Yuv444P8, full709);
    Assert.That(yuv.GetPlaneData(0).ToArray(), Is.EqualTo(new byte[] { 0, 64, 192, 255 }));
    Assert.That(yuv.GetPlaneData(1).ToArray(), Is.EqualTo(new byte[] { 128, 128, 128, 128 }));
    Assert.That(yuv.GetPlaneData(2).ToArray(), Is.EqualTo(new byte[] { 128, 128, 128, 128 }));

    Assert.That(FastRawImageConverter.Convert(yuv, PixelFormat.Bgra32).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void OddSized420UsesRoundedUpChromaAndBoxAveragesEdgeBlocks() {
    var source = new RawImage {
      Width = 3,
      Height = 3,
      Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Repeat((byte)127, 3 * 3 * 3).ToArray(),
    };

    var yuv = FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8, RawImageColorInfo.Bt709Limited);

    Assert.That(yuv.GetPlaneDimensions(1), Is.EqualTo((2, 2)));
    Assert.That(yuv.GetPlaneLength(0), Is.EqualTo(9));
    Assert.That(yuv.GetPlaneLength(1), Is.EqualTo(4));
    Assert.That(yuv.GetPlaneLength(2), Is.EqualTo(4));
    Assert.That(yuv.PixelData.Length, Is.EqualTo(17));
  }

  [Test]
  [Category("Unit")]
  public void WriterEnsureFormatNowAcceptsRgbToYuv() {
    var source = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [
        255, 0, 0, 255,
        0, 255, 0, 255,
        0, 0, 255, 255,
        255, 255, 255, 255,
      ],
    };

    var yuv = source.EnsureFormat(PixelFormat.Yuv420P8);
    Assert.That(yuv.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    Assert.That(yuv.HasEnoughPixelData, Is.True);
  }
}
