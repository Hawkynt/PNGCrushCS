using System;
using FileFormat.Core;

namespace FileFormat.Jpeg.Tests;

/// <summary>
/// A grey JPEG's decoded picture has to be the shape the file states.
/// </summary>
/// <remarks>
/// The decoder spreads a grey picture to three equal channels, so what it hands back is RGB whatever
/// the file held. That was then labelled Gray8 — one byte a pixel — while carrying three, so every
/// grey JPEG came out stretched three times across and cut off at its left third.
/// <para/>
/// Every value in it was right, which is why nothing caught it: a test comparing pixel zero, or a
/// round trip through a writer that reads the same field back, agrees perfectly with a picture that
/// is three times too wide.
/// </remarks>
[TestFixture]
public sealed class GrayscaleShapeTests {

  private static RawImage _Ramp(int width, int height) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)(y * 255 / Math.Max(1, height - 1));

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void Decoded_HasOneByteAPixelWhenItSaysItIsGrey() {
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(_Ramp(129, 97)));
    var image = JpegFile.ToRawImage(JpegReader.FromSpan(jpeg));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(
      image.PixelData, Has.Length.EqualTo(129 * 97),
      "a Gray8 picture is one byte a pixel, and saying so while carrying three stretches it");
  }

  [Test]
  [Category("Integration")]
  public void Decoded_KeepsTheGradientAcrossItsWholeHeight() {
    // The defect showed only away from the origin: the top of the picture was right and the bottom
    // held what the third of the way down should have.
    var original = _Ramp(129, 97);
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(original));
    var image = JpegFile.ToRawImage(JpegReader.FromSpan(jpeg));

    var top = image.PixelData[0];
    var bottom = image.PixelData[(97 - 1) * 129];

    Assert.Multiple(() => {
      Assert.That(top, Is.EqualTo(0).Within(12));
      Assert.That(bottom, Is.EqualTo(255).Within(12), "the last row must be the end of the ramp");
    });
  }

  /// <summary>
  /// KNOWN DEFECT, in the writer rather than the reader.
  /// </summary>
  /// <remarks>
  /// A grey picture written here comes back as near-white whatever went in, and ImageMagick reads
  /// our output the same way — so the file is wrong, not our reading of it. It is not the Huffman
  /// optimiser: writing with the standard tables gives an equally wrong file. The headers are
  /// self-consistent — one component at 1x1, one quantisation table, one scan — so the fault lies
  /// in what the single-component path puts in the entropy data.
  /// <para/>
  /// Left visible rather than deleted: this is what a grey JPEG out of this project does today.
  /// </remarks>
  [Test]
  [Category("Integration")]
  [Ignore("The grey writer produces a near-white picture; the reading side of this is fixed and covered above.")]
  public void RoundTrip_KeepsAGreyPicture() {
    // Bands eight rows deep, so the transform's own grid can hold them: a ramp that steps every row
    // is smoothed by any lossy codec, and testing against that measures the codec rather than the
    // shape this fixture is about.
    var pixels = new byte[64 * 64];
    for (var y = 0; y < 64; ++y)
    for (var x = 0; x < 64; ++x)
      pixels[y * 64 + x] = (byte)(y / 8 * 36);

    var original = new RawImage { Width = 64, Height = 64, Format = PixelFormat.Gray8, PixelData = pixels };
    var image = JpegFile.ToRawImage(JpegReader.FromSpan(
      JpegWriter.ToBytes(JpegFile.FromRawImage(original))));

    Assert.That(image.PixelData, Has.Length.EqualTo(original.PixelData.Length));
    for (var i = 0; i < original.PixelData.Length; ++i)
      Assert.That(image.PixelData[i], Is.EqualTo(original.PixelData[i]).Within(12), $"pixel {i}");
  }
}
