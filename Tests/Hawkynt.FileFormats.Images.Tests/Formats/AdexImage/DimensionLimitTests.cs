using System;
using FileFormat.Core;
using FileFormat.ComputerEyes;
using FileFormat.HayesJtfax;
using FileFormat.Nifti;
using FileFormat.Ps2Txc;
using FileFormat.SbigCcd;
using FileFormat.SifImage;
using FileFormat.SonyMavica;
using FileFormat.Tim2;

namespace FileFormat.AdexImage.Tests;

/// <summary>
/// The formats whose header states its size in sixteen bits refuse a picture bigger than that.
/// </summary>
/// <remarks>
/// Truncating instead would produce a file that reads back as a different picture rather than as a
/// broken one, and a wrong picture that opens is worse than a right one that will not.
/// </remarks>
[TestFixture]
public sealed class DimensionLimitTests {

  /// <summary>Claims a size without allocating it — the guard runs before anything reads a pixel.</summary>
  private static RawImage _Claiming(int width, int height)
    => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = [] };

  [Test]
  [Category("Unit")]
  public void FromRawImage_RefusesAPictureWiderThanTheHeaderCanState() {
    var tooWide = _Claiming(70000, 4);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => AdexImageFile.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => SifImageFile.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => SonyMavicaFile.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => Ps2TxcFile.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => HayesJtfaxFile.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => ComputerEyesFile.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => SbigCcdFile.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => Tim2File.FromRawImage(tooWide));
      Assert.Throws<ArgumentException>(() => NiftiFile.FromRawImage(tooWide));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RefusesAPictureTallerThanTheHeaderCanState() {
    var tooTall = _Claiming(4, 70000);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => AdexImageFile.FromRawImage(tooTall));
      Assert.Throws<ArgumentException>(() => Tim2File.FromRawImage(tooTall));
      Assert.Throws<ArgumentException>(() => NiftiFile.FromRawImage(tooTall));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RefusesAPictureWithNoPixelsInIt() {
    Assert.Throws<ArgumentException>(() => AdexImageFile.FromRawImage(_Claiming(0, 0)));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_StopsWhereEachFormatsOwnHeaderDoes() {
    // NIfTI's dim[] is signed, so it stops an octave earlier than the rest.
    Assert.Multiple(() => {
      Assert.That(AdexImageFile.MaxDimension, Is.EqualTo(65535));
      Assert.That(NiftiFile.MaxDimension, Is.EqualTo(32767));
      Assert.Throws<ArgumentException>(() => NiftiFile.FromRawImage(_Claiming(40000, 4)));
    });
  }
}
