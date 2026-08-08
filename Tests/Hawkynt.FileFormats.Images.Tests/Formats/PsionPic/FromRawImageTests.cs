using System;
using FileFormat.Core;

namespace FileFormat.PsionPic.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A width that is neither a multiple of eight nor of sixteen, the two paddings at play.</summary>
  private const int _WIDTH = 37;

  private const int _HEIGHT = 11;

  private static RawImage _Checkers(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var value = (byte)(((x * 3 + y * 5) & 1) == 0 ? 255 : 0);
      var at = (y * width + x) * 3;
      data[at] = data[at + 1] = data[at + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoColours_ReproducesExactly() {
    var source = _Checkers(_WIDTH, _HEIGHT);
    var file = PsionPicFile.FromRawImage(source);
    var decoded = PsionPicFile.ToRawImage(PsionPicReader.FromBytes(PsionPicWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    // The record states the size, so there is nothing to scale a picture to.
    var wide = PsionPicFile.FromRawImage(_Checkers(200, 3));
    var tall = PsionPicFile.FromRawImage(_Checkers(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PadsRowsToWholeWords() {
    // Thirty-seven pixels take five bytes, but a row is a whole number of sixteen-bit words: six.
    var bytes = PsionPicWriter.ToBytes(PsionPicFile.FromRawImage(_Checkers(_WIDTH, _HEIGHT)));

    Assert.That(bytes, Has.Length.EqualTo(PsionPicFile.FirstRecord + PsionPicFile.RecordSize + 6 * _HEIGHT));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RunsTheBitsFromTheLeastSignificantEnd() {
    var pixels = new byte[8];
    pixels[0] = 1;

    var file = PsionPicFile.FromRawImage(new() {
      Width = 8, Height = 1, Format = PixelFormat.Indexed8, PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0], PaletteCount = 2,
    });

    // The leftmost pixel is bit 0, not bit 7 — the mistake that mirrors every glyph.
    Assert.That(PsionPicWriter.ToBytes(file)[PsionPicFile.FirstRecord + PsionPicFile.RecordSize], Is.EqualTo(1));
  }
}
