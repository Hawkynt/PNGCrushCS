using System;
using System.Text;
using FileFormat.ChinonEs1000;
using FileFormat.Core;
using FileFormat.Fff;
using FileFormat.Hta;
using FileFormat.Illustrator;
using FileFormat.KodakDc25;
using FileFormat.PicturePublisher;
using FileFormat.PicturePublisher4;
using FileFormat.PostScript;
using FileFormat.SonyPmp;
using FileFormat.Xar;

namespace Hawkynt.FileFormats.Images.Tests.Formats.GapClosures;

[TestFixture]
public sealed class AdditionalWriterClosureTests {

  private static RawImage _Pattern(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 17 + y * 3);
      pixels[at + 1] = (byte)(x * 5 + y * 29);
      pixels[at + 2] = (byte)(255 - x * 7 - y * 11);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _SmoothPattern(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(32 + 176L * x / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(24 + 184L * y / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)(48 + 144L * (x + y) / Math.Max(1, width + height - 2));
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static double _Mae(byte[] expected, byte[] actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));
    long absoluteError = 0;
    for (var i = 0; i < expected.Length; ++i)
      absoluteError += Math.Abs(expected[i] - actual[i]);
    return absoluteError / (double)expected.Length;
  }

  [Test]
  [Category("Unit")]
  public void Hta_OneMemberPngRoundTrip_IsPixelExact() {
    var source = _Pattern(23, 17);
    var bytes = HtaWriter.ToBytes(HtaFile.FromRawImage(source));
    var decodedFile = HtaReader.FromBytes(bytes);
    var decoded = HtaFile.ToRawImage(decodedFile);

    Assert.Multiple(() => {
      Assert.That(HtaFile.ImageCount(decodedFile), Is.EqualTo(1));
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void PicturePublisher4_TiffWrapperRoundTrip_IsPixelExact() {
    var source = _Pattern(23, 17);
    var bytes = PicturePublisher4Writer.ToBytes(PicturePublisher4File.FromRawImage(source));
    var decoded = PicturePublisher4File.ToRawImage(PicturePublisher4Reader.FromBytes(bytes));
    Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void PicturePublisher5_SingleObjectRoundTrip_IsPixelExact() {
    var source = _Pattern(23, 17);
    var bytes = PicturePublisherWriter.ToBytes(PicturePublisherFile.FromRawImage(source));
    var decoded = PicturePublisherFile.ToRawImage(PicturePublisherReader.FromBytes(bytes));
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void PostScript_ColorImageRoundTrip_PreservesDeclaredRasterSize() {
    var source = _Pattern(23, 17);
    var bytes = PostScriptWriter.ToBytes(PostScriptFile.FromRawImage(source));
    var parsed = PostScriptReader.FromBytes(bytes);
    var rendering = PostScriptRenderer.Render(parsed);

    Assert.Multiple(() => {
      Assert.That(rendering.Image.Width, Is.EqualTo(source.Width));
      Assert.That(rendering.Image.Height, Is.EqualTo(source.Height));
      Assert.That(rendering.HasInk, Is.True);
      Assert.That(rendering.PagesShown, Is.GreaterThanOrEqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void Illustrator6_NativeXiRasterRoundTrip_IsPixelExactAndIdentifiesNativeSyntax() {
    var source = _Pattern(29, 21);
    var bytes = AiWriter.ToBytes(AiFile.FromRawImage(source));
    var text = Encoding.ASCII.GetString(bytes);
    var parsed = AiReader.FromBytes(bytes);
    var decoded = AiFile.ToRawImage(parsed);

    Assert.Multiple(() => {
      Assert.That(parsed.Raster, Is.Not.Null);
      Assert.That(parsed.Version, Does.Contain("AI5_FileFormat 2.0"));
      Assert.That(text, Does.Contain("%AI5_BeginRaster"));
      Assert.That(text, Does.Contain("8 3 0 0 0 0 XI"));
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Xar_RealBitmapObjectRoundTrip_IsPixelExactWithoutPreviewFallback() {
    var source = _Pattern(31, 19);
    var bytes = XarWriter.ToBytes(XarFile.FromRawImage(source));
    var parsed = XarReader.FromBytes(bytes);
    var decoded = XarFile.ToRawImage(parsed);

    Assert.Multiple(() => {
      Assert.That(parsed.Bitmap, Is.Not.Null);
      Assert.That(parsed.Preview, Is.Null);
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChinonEs1000_InverseCfaProducesLegalCameraSizedRawWithBoundedSmoothImageError() {
    var source = _SmoothPattern(ChinonEs1000File.Width, ChinonEs1000File.Height);
    var bytes = ChinonEs1000Writer.ToBytes(ChinonEs1000File.FromRawImage(source));
    var decoded = ChinonEs1000File.ToRawImage(ChinonEs1000Reader.FromBytes(bytes));
    var mae = _Mae(source.PixelData, decoded.PixelData);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(ChinonEs1000File.FileSize));
      Assert.That(bytes.AsSpan(0, ChinonEs1000File.Magic.Length).ToArray(), Is.EqualTo(ChinonEs1000File.Magic.ToArray()));
      Assert.That(decoded.Width, Is.EqualTo(ChinonEs1000File.Width));
      Assert.That(decoded.Height, Is.EqualTo(ChinonEs1000File.Height));
      Assert.That(mae, Is.LessThan(80.0), $"inverse sensor synthesis MAE was {mae:F2}");
    });
  }

  [Test]
  [Category("Unit")]
  public void KodakDc25_InverseComplementaryCfaWritesMetadataValidRawWithBoundedError() {
    var source = _SmoothPattern(KodakDc25File.WideOutputWidth, KodakDc25File.WideOutputHeight);
    var bytes = KodakDc25Writer.ToBytes(KodakDc25File.FromRawImage(source));
    var decoded = KodakDc25File.ToRawImage(KodakDc25Reader.FromBytes(bytes));
    var mae = _Mae(source.PixelData, decoded.PixelData);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(KodakDc25File.SensorOffset + KodakDc25File.WideSensorWidth * KodakDc25File.SensorHeight));
      Assert.That(bytes.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { (byte)'M', (byte)'M', 0, 42 }));
      Assert.That(Encoding.ASCII.GetString(bytes, 0, KodakDc25File.SensorOffset), Does.Contain(KodakDc25File.Model));
      Assert.That(decoded.Width, Is.EqualTo(KodakDc25File.WideOutputWidth));
      Assert.That(decoded.Height, Is.EqualTo(KodakDc25File.WideOutputHeight));
      Assert.That(mae, Is.LessThan(70.0), $"inverse complementary sensor synthesis MAE was {mae:F2}");
    });
  }

  [TestCase("SonyPmp")]
  [TestCase("MaggiFff")]
  [Category("Unit")]
  public void FixedOffsetJpegRecords_PreserveImageDimensions(string format) {
    var source = _Pattern(19, 13);
    RawImage decoded = format switch {
      "SonyPmp" => SonyPmpFile.ToRawImage(SonyPmpReader.FromBytes(SonyPmpWriter.ToBytes(SonyPmpFile.FromRawImage(source)))),
      "MaggiFff" => FffFile.ToRawImage(FffReader.FromBytes(FffWriter.ToBytes(FffFile.FromRawImage(source)))),
      _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
    });
  }
}
