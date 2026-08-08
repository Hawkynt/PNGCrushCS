using System;
using FileFormat.Core;

namespace FileFormat.Pl4Picture.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The eight levels a three-bit channel expands to, which is what the palette can say.</summary>
  private static readonly byte[] _Levels = [0, 36, 73, 109, 146, 182, 219, 255];

  /// <summary>Sixteen colours the ST palette holds exactly, in bands narrow enough to test the
  /// word-by-word interleaving of the planes.</summary>
  private static RawImage _Bands(int width, int height) {
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x / 3 + y) % 16;
      var at = (y * width + x) * 3;
      data[at] = _Levels[index >> 1];
      data[at + 1] = _Levels[(index * 3) % 8];
      data[at + 2] = _Levels[index % 2 * 7];
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenStColours_ReproducesExactly() {
    var source = _Bands(Pl4PictureFile.Width, Pl4PictureFile.Height);
    var file = Pl4PictureFile.FromRawImage(source);
    var decoded = Pl4PictureFile.ToRawImage(Pl4PictureReader.FromBytes(Pl4PictureWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((Pl4PictureFile.Width, Pl4PictureFile.Height)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnyOtherSize() {
    // The screen is one size and no other, so a picture of another is brought to it.
    var file = Pl4PictureFile.FromRawImage(_Bands(37, 11));

    Assert.That(file.Unpacked, Has.Length.EqualTo(Pl4PictureFile.UnpackedSize));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesBothScreensTheSame() {
    // The two are shown alternately and averaged, so a picture drawn on one and not the other comes
    // out half way to the other's colours — writing both alike is what leaves the average alone.
    var file = Pl4PictureFile.FromRawImage(_Bands(Pl4PictureFile.Width, Pl4PictureFile.Height));

    const int screen = 32 + Pl4PictureFile.Width * Pl4PictureFile.Height / 2;

    Assert.That(
      file.Unpacked.AsSpan(Pl4PictureFile.FirstPaletteOffset, screen).ToArray(),
      Is.EqualTo(file.Unpacked.AsSpan(Pl4PictureFile.SecondPaletteOffset, screen).ToArray()));
  }
}
