using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MsxGl6;

namespace FileFormat.MsxGl6.Tests;

[TestFixture]
public sealed class MsxGl6Tests {

  private const int _WIDTH = 64;
  private const int _DISPLAY_HEIGHT = 48;

  /// <summary>Four flat colours in vertical bands, so quantisation has an exact answer.</summary>
  private static RawImage _Bands() {
    ReadOnlySpan<byte> colors = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
    var data = new byte[_WIDTH * _DISPLAY_HEIGHT * 3];
    for (var y = 0; y < _DISPLAY_HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var band = x / (_WIDTH / 4);
      var o = (y * _WIDTH + x) * 3;
      data[o] = colors[band * 3];
      data[o + 1] = colors[band * 3 + 1];
      data[o + 2] = colors[band * 3 + 2];
    }

    return new() { Width = _WIDTH, Height = _DISPLAY_HEIGHT, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  public void Written_StoresHalfTheScanlinesAtTwoBitsPerPixel() {
    var file = MsxGl6File.FromRawImage(_Bands());
    var bytes = MsxGl6Writer.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(file.Height, Is.EqualTo(_DISPLAY_HEIGHT / 2), "a stored row covers two scanlines");
      Assert.That(bytes, Has.Length.EqualTo(MsxGl6File.HeaderSize + _WIDTH * (_DISPLAY_HEIGHT / 2) / 4));
      Assert.That(bytes[0] | (bytes[1] << 8), Is.EqualTo(_WIDTH));
      Assert.That(bytes[2] | (bytes[3] << 8), Is.EqualTo(_DISPLAY_HEIGHT / 2));
    });
  }

  [Test]
  public void RoundTrip_PreservesThePixels() {
    var file = MsxGl6File.FromRawImage(_Bands());
    var reread = MsxGl6Reader.FromBytes(MsxGl6Writer.ToBytes(file));

    Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void Decoded_ShowsEachStoredRowTwiceAndKeepsFourFlatColors() {
    var file = MsxGl6File.FromRawImage(_Bands());
    var decoded = MsxGl6File.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(_WIDTH));
      Assert.That(decoded.Height, Is.EqualTo(_DISPLAY_HEIGHT));
      Assert.That(decoded.PaletteCount, Is.EqualTo(MsxGl6File.ColorCount));
    });

    // Every scanline pair must be identical, and each band must be one solid index.
    for (var y = 0; y < _DISPLAY_HEIGHT; y += 2)
    for (var x = 0; x < _WIDTH; ++x)
      Assert.That(decoded.PixelData[(y + 1) * _WIDTH + x], Is.EqualTo(decoded.PixelData[y * _WIDTH + x]),
        $"scanline pair at {x},{y}");

    for (var band = 0; band < 4; ++band) {
      var expected = decoded.PixelData[band * (_WIDTH / 4)];
      for (var x = band * (_WIDTH / 4); x < (band + 1) * (_WIDTH / 4); ++x)
        Assert.That(decoded.PixelData[x], Is.EqualTo(expected), $"band {band} at x={x}");
    }

    Assert.That(new[] { decoded.PixelData[0], decoded.PixelData[16], decoded.PixelData[32], decoded.PixelData[48] },
      Is.Unique, "four distinct bands must map to four distinct indices");
  }

  [Test]
  public void WithoutAPalette_TheDefaultIsBlackOnWhite() {
    var decoded = MsxGl6File.ToRawImage(new() { Width = 8, Height = 2, PixelData = new byte[4], Palette = [] });

    Assert.Multiple(() => {
      Assert.That(decoded.Palette![..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
      Assert.That(decoded.Palette![3..6], Is.EqualTo(new byte[] { 0, 0, 0 }));
    });
  }

  [Test]
  public void Reader_RejectsAnImpossibleHeader() {
    Assert.Throws<InvalidDataException>(() => MsxGl6Reader.FromBytes([0, 0, 0, 0, 0]));
  }

  [Test]
  public void Reader_RejectsAFileTooShortForItsHeader() {
    Assert.Throws<InvalidDataException>(() => MsxGl6Reader.FromBytes([64, 0, 64, 0, 1, 2, 3]));
  }

  [Test]
  public void FromRawImage_RejectsAnOddHeight() {
    var image = new RawImage { Width = 8, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[8 * 3 * 3] };

    Assert.Throws<ArgumentException>(() => MsxGl6File.FromRawImage(image));
  }
}
