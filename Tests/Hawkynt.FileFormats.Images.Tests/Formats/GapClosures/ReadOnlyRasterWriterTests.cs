using System;
using FileFormat.Arn;
using FileFormat.ByLight;
using FileFormat.CImage;
using FileFormat.Core;
using FileFormat.CoreIdc;
using FileFormat.DispThumbnail;
using FileFormat.Iss;
using FileFormat.JigsawPicture;
using FileFormat.LaserData;
using FileFormat.NcrImage;
using FileFormat.Optocat;
using FileFormat.PlaybackBitmapSequence;
using FileFormat.SecretPhotos;
using FileFormat.Skantek;
using FileFormat.TilePic;
using FileFormat.XionicsSmp;
using FileFormat.RicohIs30;

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
  public void Group4Formats_OddWidthMultiRowRoundTrip_IsPixelExact(string format) {
    // 19 pixels deliberately crosses byte boundaries at a different bit position on every row if a
    // continuous Indexed1 stream is incorrectly handed to a row-padded fax format.
    var source = _Checker(19, 13);
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
  public void CoreIdc_LosslessPlanarRgbRoundTrip_IsPixelExact() {
    var source = _Checker(23, 17);
    var encoded = CoreIdcWriter.ToBytes(CoreIdcFile.FromRawImage(source));
    var decoded = CoreIdcFile.ToRawImage(CoreIdcReader.FromBytes(encoded));
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Optocat_LosslessRgbRoundTrip_IsPixelExact() {
    var source = _Checker(23, 17);
    var encoded = OptocatWriter.ToBytes(OptocatFile.FromRawImage(source));
    var decoded = OptocatFile.ToRawImage(OptocatReader.FromBytes(encoded));
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ByteAlignedScannerFormats_PadOnlyRightEdge() {
    var source = _Checker(19, 7);

    var xionics = XionicsSmpFile.ToRawImage(XionicsSmpReader.FromBytes(
      XionicsSmpWriter.ToBytes(XionicsSmpFile.FromRawImage(source))));
    var ricoh = RicohIs30File.ToRawImage(RicohIs30Reader.FromBytes(
      RicohIs30Writer.ToBytes(RicohIs30File.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(xionics.Width, Is.EqualTo(24));
      Assert.That(ricoh.Width, Is.EqualTo(20));
      Assert.That(xionics.Height, Is.EqualTo(source.Height));
      Assert.That(ricoh.Height, Is.EqualTo(source.Height));
    });

    var xRgb = xionics.ToRgb24();
    var rRgb = ricoh.ToRgb24();
    for (var y = 0; y < source.Height; ++y)
    for (var x = 0; x < source.Width; ++x) {
      var src = (y * source.Width + x) * 3;
      var xd = (y * xionics.Width + x) * 3;
      var rd = (y * ricoh.Width + x) * 3;
      Assert.Multiple(() => {
        Assert.That(xRgb[xd], Is.EqualTo(source.PixelData[src]));
        Assert.That(rRgb[rd], Is.EqualTo(source.PixelData[src]));
      });
    }
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

  [TestCase("ByLight")]
  [TestCase("DispThumbnail")]
  [TestCase("SecretPhotos")]
  [Category("Unit")]
  public void JpegWrappers_PreserveImageDimensions(string format) {
    var source = _Checker(17, 11);
    RawImage decoded = format switch {
      "ByLight" => ByLightFile.ToRawImage(ByLightReader.FromBytes(ByLightWriter.ToBytes(ByLightFile.FromRawImage(source)))),
      "DispThumbnail" => DispThumbnailFile.ToRawImage(DispThumbnailReader.FromBytes(DispThumbnailWriter.ToBytes(DispThumbnailFile.FromRawImage(source)))),
      "SecretPhotos" => SecretPhotosFile.ToRawImage(SecretPhotosReader.FromBytes(SecretPhotosWriter.ToBytes(SecretPhotosFile.FromRawImage(source)))),
      _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
    });
  }

  [TestCase("Playback")]
  [TestCase("Jigsaw")]
  [Category("Unit")]
  public void BmpWrappers_RoundTripThroughTheirOwnReaders(string format) {
    var source = _Checker(21, 9);
    RawImage decoded = format switch {
      "Playback" => PlaybackBitmapSequenceFile.ToRawImage(PlaybackBitmapSequenceReader.FromBytes(
        PlaybackBitmapSequenceWriter.ToBytes(PlaybackBitmapSequenceFile.FromRawImage(source)))),
      "Jigsaw" => JigsawPictureFile.ToRawImage(JigsawPictureReader.FromBytes(
        JigsawPictureWriter.ToBytes(JigsawPictureFile.FromRawImage(source)))),
      _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
    });
  }
}
