using System;
using FileFormat.Core;

namespace FileFormat.PhotoChromePcs.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>The eight levels a three-bit channel expands to, which is what the palette can say.</summary>
  private static readonly byte[] _Levels = [0, 36, 73, 109, 146, 182, 219, 255];

  /// <summary>
  /// Sixteen colours the ST palette holds exactly, changing along the line often enough to cross
  /// every place the palette is reloaded.
  /// </summary>
  private static RawImage _Bands(int width, int height) {
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x / 5 + y) % 16;
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
    var source = _Bands(PhotoChromePcsFile.Width, PhotoChromePcsFile.Height);
    var file = PhotoChromePcsFile.FromRawImage(source);
    var decoded = PhotoChromePcsFile.ToRawImage(
      PhotoChromePcsReader.FromBytes(PhotoChromePcsWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(
        (decoded.Width, decoded.Height),
        Is.EqualTo((PhotoChromePcsFile.Width, PhotoChromePcsFile.Height)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAPictureOfAnyOtherSize() {
    // The screen is one size and no other, and its height is one short of a full one.
    var file = PhotoChromePcsFile.FromRawImage(_Bands(37, 11));

    Assert.Multiple(() => {
      Assert.That(file.Fields, Has.Length.EqualTo(1));
      Assert.That(file.Fields[0], Has.Length.EqualTo(PhotoChromePcsFile.FieldSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SaysTheSameColourInEveryZoneOfEveryLine() {
    // The palette is reloaded up to three times across a line, at thresholds that are cycle counts
    // rather than anything the file states. A colour written into all four reloads reads the same
    // whatever a decoder believes about where they happen.
    var field = PhotoChromePcsFile.FromRawImage(
      _Bands(PhotoChromePcsFile.Width, PhotoChromePcsFile.Height)).Fields[0];

    var first = field.AsSpan(PhotoChromePcsFile.BitmapSize, PhotoChromePcsFile.ColorCount * 2).ToArray();

    Assert.Multiple(() => {
      for (var zone = 1; zone < PhotoChromePcsFile.Zones; ++zone)
        Assert.That(
          field.AsSpan(
            PhotoChromePcsFile.BitmapSize + zone * PhotoChromePcsFile.ColorCount * 2,
            PhotoChromePcsFile.ColorCount * 2).ToArray(),
          Is.EqualTo(first),
          $"zone {zone}");
    });
  }
}
