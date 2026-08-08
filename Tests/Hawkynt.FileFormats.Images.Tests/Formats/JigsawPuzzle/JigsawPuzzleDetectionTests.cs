using FileFormat.Core;
using FileFormat.AutoFx;
using FileFormat.InterleafImage;
using FileFormat.JigsawPuzzle;
using Hawkynt.FileFormats.Images;

namespace FileFormat.JigsawPuzzle.Tests;

/// <summary>
/// A good share of what cannot be read is a format that decodes perfectly, reached under a name
/// somebody else's format holds. A reader found only by its extension is one rename from being no
/// reader at all, so each of these has to be recognised by what the file says about itself.
/// </summary>
[TestFixture]
public sealed class ContentDetectionTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 9);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void DetectFromBytes_AJigsawPuzzleIsFoundByItsArithmetic()
    => Assert.That(
      FormatRegistry.DetectFromBytes(JigsawPuzzleWriter.ToBytes(JigsawPuzzleFile.FromRawImage(_Picture(16, 9)))),
      Is.EqualTo(ImageFormat.JigsawPuzzle));

  [Test]
  [Category("Unit")]
  public void DetectFromBytes_AnAutoFxPictureIsFoundByItsSignature()
    => Assert.That(
      FormatRegistry.DetectFromBytes(AutoFxWriter.ToBytes(AutoFxFile.FromRawImage(_Picture(16, 9)))),
      Is.EqualTo(ImageFormat.AutoFx));

  [Test]
  [Category("Unit")]
  public void DetectFromBytes_AnInterleafImageIsFoundByItsSignature()
    => Assert.That(
      FormatRegistry.DetectFromBytes(InterleafImageWriter.ToBytes(InterleafImageFile.FromRawImage(_Picture(16, 9)))),
      Is.EqualTo(ImageFormat.InterleafImage));
}
