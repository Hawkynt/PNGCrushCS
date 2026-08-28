using System;
using FileFormat.Arn;
using FileFormat.ByLight;
using FileFormat.CImage;
using FileFormat.Core;
using FileFormat.Iss;
using FileFormat.LaserData;
using FileFormat.NcrImage;
using FileFormat.Skantek;
using FileFormat.TilePic;

namespace Hawkynt.FileFormats.Images.Tests.Formats.GapClosures;

[TestFixture]
public sealed class ReadOnlyRasterWriterTests {

  private static RawImage _Checker(int width = 19, int height = 13) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var v = ((x ^ y) & 1) == 0 ? (byte)0 : (byte)255;
      var at = (y * width + x) * 3;
      pixels[at] = v;
      pixels[at + 1] = v;
      pixels[at + 2] = v;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Iss_ArbitraryImageRoundTrip_IsPixelExactForGray() {
    var source = _Checker();
    var encoded = IssWriter.ToBytes(IssFile.FromRawImage(source));
    var decoded = IssFile.ToRawImage(IssReader.FromBytes(encoded));
    Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Arn_ArbitraryImageRoundTrip_PreservesIndexedChecker() {
    var source = _Checker();
    var encoded = ArnWriter.ToBytes(ArnFile.FromRawImage(source));
    var decoded = ArnFile.ToRawImage(ArnReader.FromBytes(encoded));
    Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
  }

  [TestCase("CImage")]
  [TestCase("Skantek")]
  [TestCase("Ncr")]
  [TestCase("LaserData")]
  [Category("Unit")]
  public void Group4Formats_ArbitraryImageRoundTrip_IsPixelExact(string format) {
    var source = _Checker();
    RawImage decoded = format switch {
      "CImage" => CImageFile.ToRawImage(CImageReader.FromBytes(CImageWriter.ToBytes(CImageFile.FromRawImage(source)))),
      "Skantek" => SkantekFile.ToRawImage(SkantekReader.FromBytes(SkantekWriter.ToBytes(SkantekFile.FromRawImage(source)))),
      "Ncr" => NcrImageFile.ToRawImage(NcrImageReader.FromBytes(NcrImageWriter.ToBytes(NcrImageFile.FromRawImage(source)))),
      "LaserData" => LaserDataFile.ToRawImage(LaserDataReader.FromBytes(LaserDataWriter.ToBytes(LaserDataFile.FromRawImage(source)))),
      _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
    Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void TilePic_WriterBuildsValidSingleLayerJpegContainer() {
    var source = _Checker(17, 11);
    var encoded = TilePicWriter.ToBytes(TilePicFile.FromRawImage(source));
    var decoded = TilePicReader.FromBytes(encoded);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.PixelData.Length, Is.EqualTo(source.PixelData.Length));
    });
  }

  [Test]
  [Category("Unit")]
  public void ByLight_WriterBuildsValidJpegContainer() {
    var source = _Checker(17, 11);
    var encoded = ByLightWriter.ToBytes(ByLightFile.FromRawImage(source));
    var decoded = ByLightFile.ToRawImage(ByLightReader.FromBytes(encoded));
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
    });
  }
}
