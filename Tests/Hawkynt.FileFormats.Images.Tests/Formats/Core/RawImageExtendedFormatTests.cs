using System;
using FileFormat.Core;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class RawImageExtendedFormatTests {

  [Test]
  [Category("Unit")]
  public void OddSizedYuv420PlanesRoundChromaUpIndividually() {
    var image = new RawImage {
      Width = 3,
      Height = 3,
      Format = PixelFormat.Yuv420P8,
      PixelData = new byte[17],
    };

    Assert.That(image.MinimumPixelDataLength, Is.EqualTo(17));
    Assert.That(image.PlaneCount, Is.EqualTo(3));
    Assert.That(image.GetPlaneDimensions(0), Is.EqualTo((3, 3)));
    Assert.That(image.GetPlaneDimensions(1), Is.EqualTo((2, 2)));
    Assert.That(image.GetPlaneDimensions(2), Is.EqualTo((2, 2)));
    Assert.That(image.GetPlaneOffset(0), Is.EqualTo(0));
    Assert.That(image.GetPlaneOffset(1), Is.EqualTo(9));
    Assert.That(image.GetPlaneOffset(2), Is.EqualTo(13));
    Assert.That(image.GetPlaneLength(0), Is.EqualTo(9));
    Assert.That(image.GetPlaneLength(1), Is.EqualTo(4));
    Assert.That(image.GetPlaneLength(2), Is.EqualTo(4));
    Assert.That(image.HasEnoughPixelData, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Yuv420LimitedBlackConvertsAtTheWriterBoundary() {
    // 2x2 Y plane followed by one U and one V sample.
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = [16, 16, 16, 16, 128, 128],
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };

    var bgra = image.ToBgra32();

    Assert.That(bgra, Is.EqualTo(new byte[] {
      0, 0, 0, 255,
      0, 0, 0, 255,
      0, 0, 0, 255,
      0, 0, 0, 255,
    }));
  }

  [Test]
  [Category("Unit")]
  public void Yuv420FullWhiteDoesNotGetStudioRangeExpandedTwice() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = [255, 255, 255, 255, 128, 128],
      ColorInfo = new() {
        Range = RawColorRange.Full,
        Matrix = RawMatrixCoefficients.Bt601,
      },
    };

    var rgb = image.ToRgb24();
    foreach (var value in rgb)
      Assert.That(value, Is.InRange(254, 255));
  }

  [Test]
  [Category("Unit")]
  public void TenBitYuvUsesRightJustifiedUshortSamplesAndExactPlaneSizes() {
    // 2x2 4:2:0: four Y samples + U + V, six ushort samples = 12 bytes.
    var data = new byte[12];
    for (var i = 0; i < 4; ++i)
      _WriteU16(data, i * 2, 64); // limited-range black at 10 bit
    _WriteU16(data, 8, 512);
    _WriteU16(data, 10, 512);

    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv420P10,
      PixelData = data,
      ColorInfo = RawImageColorInfo.Bt709Limited,
    };

    Assert.That(RawImage.YuvBitDepth(image.Format), Is.EqualTo(10));
    Assert.That(RawImage.BitsPerPixel(image.Format), Is.EqualTo(24));
    Assert.That(image.MinimumPixelDataLength, Is.EqualTo(12));
    Assert.That(image.ToBgra32(), Is.EqualTo(new byte[] {
      0, 0, 0, 255,
      0, 0, 0, 255,
      0, 0, 0, 255,
      0, 0, 0, 255,
    }));
  }

  [Test]
  [Category("Unit")]
  public void HalfFloatRoundTripPreservesHdrValuesAboveOne() {
    var half = new byte[6];
    _WriteHalf(half, 0, (Half)2.0f);
    _WriteHalf(half, 2, (Half)0.5f);
    _WriteHalf(half, 4, (Half)(-0.25f));

    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.RgbF16,
      PixelData = half,
    };

    var f32 = RawImageConverter.Convert(image, PixelFormat.RgbF32);

    Assert.That(BitConverter.ToSingle(f32.PixelData, 0), Is.EqualTo(2.0f));
    Assert.That(BitConverter.ToSingle(f32.PixelData, 4), Is.EqualTo(0.5f));
    Assert.That(BitConverter.ToSingle(f32.PixelData, 8), Is.EqualTo(-0.25f));

    // Narrowing is intentionally deferred to the consumer. When requested, it is explicit clipping.
    Assert.That(image.ToRgb24(), Is.EqualTo(new byte[] { 255, 128, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void FloatingPointFormatsReportTheirPhysicalStorage() {
    Assert.That(RawImage.BytesPerPixel(PixelFormat.RgbF16), Is.EqualTo(6));
    Assert.That(RawImage.BitsPerPixel(PixelFormat.RgbF16), Is.EqualTo(48));
    Assert.That(RawImage.BytesPerPixel(PixelFormat.RgbaF32), Is.EqualTo(16));
    Assert.That(RawImage.BitsPerPixel(PixelFormat.RgbaF32), Is.EqualTo(128));
  }

  private static void _WriteU16(byte[] data, int offset, int value) {
    data[offset] = (byte)value;
    data[offset + 1] = (byte)(value >> 8);
  }

  private static void _WriteHalf(byte[] data, int offset, Half value) {
    var bits = BitConverter.HalfToUInt16Bits(value);
    data[offset] = (byte)bits;
    data[offset + 1] = (byte)(bits >> 8);
  }
}
