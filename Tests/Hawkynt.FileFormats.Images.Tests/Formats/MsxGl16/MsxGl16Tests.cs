using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MsxGl16;

namespace FileFormat.MsxGl16.Tests;

[TestFixture]
public sealed class MsxGl16Tests {

  private static RawImage _Bands(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 3;
      var level = (byte)(x * 255 / Math.Max(1, width - 1));
      data[o] = level;
      data[o + 1] = (byte)(255 - level);
      data[o + 2] = (byte)(y * 255 / Math.Max(1, height - 1));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  public void Written_IsAFourByteHeaderThenTwoPixelsPerByte() {
    var bytes = MsxGl16Writer.ToBytes(MsxGl16File.FromRawImage(_Bands(64, 48)));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(4 + 64 * 48 / 2));
      Assert.That(bytes[0] | (bytes[1] << 8), Is.EqualTo(64));
      Assert.That(bytes[2] | (bytes[3] << 8), Is.EqualTo(48));
    });
  }

  [Test]
  public void RoundTrip_PreservesTheBitmap() {
    var file = MsxGl16File.FromRawImage(_Bands(64, 48));
    var reread = MsxGl16Reader.FromBytes(MsxGl16Writer.ToBytes(file));

    Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void Nibbles_AreStoredHighHalfFirst() {
    // Two adjacent pixels of known indices must land in one byte, left one in the high nibble.
    var data = new byte[] { 2, 0, 1, 0, 0x5A };
    var decoded = MsxGl16File.ToRawImage(MsxGl16Reader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(decoded.PixelData[0], Is.EqualTo(5));
      Assert.That(decoded.PixelData[1], Is.EqualTo(10));
    });
  }

  [Test]
  public void Screen7_DrawsEveryStoredRowOnTwoScanlines() {
    var stored = MsxGl16Reader.FromSpan(new byte[] { 4, 0, 2, 0, 0x01, 0x23, 0x45, 0x67 }, MsxGl16Mode.Screen7);
    var decoded = MsxGl16File.ToRawImage(stored);

    Assert.Multiple(() => {
      Assert.That(decoded.Height, Is.EqualTo(4));
      for (var x = 0; x < 4; ++x) {
        Assert.That(decoded.PixelData[1 * 4 + x], Is.EqualTo(decoded.PixelData[x]), $"row pair 0 at x={x}");
        Assert.That(decoded.PixelData[3 * 4 + x], Is.EqualTo(decoded.PixelData[2 * 4 + x]), $"row pair 1 at x={x}");
      }
    });
  }

  [Test]
  public void Screen5_DrawsOneScanlinePerStoredRow() {
    var decoded = MsxGl16File.ToRawImage(MsxGl16Reader.FromSpan(new byte[] { 4, 0, 2, 0, 0x01, 0x23, 0x45, 0x67 }));

    Assert.That(decoded.Height, Is.EqualTo(2));
  }

  [Test]
  public void ModeFromExtension_FollowsTheScreenNumber() {
    Assert.Multiple(() => {
      Assert.That(MsxGl16File.ModeFromExtension(".gl7"), Is.EqualTo(MsxGl16Mode.Screen7));
      Assert.That(MsxGl16File.ModeFromExtension(".SH7"), Is.EqualTo(MsxGl16Mode.Screen7));
      Assert.That(MsxGl16File.ModeFromExtension(".gl5"), Is.EqualTo(MsxGl16Mode.Screen5));
      Assert.That(MsxGl16File.ModeFromExtension(".sh5"), Is.EqualTo(MsxGl16Mode.Screen5));
    });
  }

  [Test]
  public void WithoutACompanionPalette_TheStartupColorsApply() {
    var decoded = MsxGl16File.ToRawImage(MsxGl16Reader.FromBytes([2, 0, 1, 0, 0x10]));
    var expected = MsxGraphics.PaletteToRgb(MsxGraphics.DefaultPalette, MsxGl16File.ColorCount);

    Assert.That(decoded.Palette, Is.EqualTo(expected));
  }

  [Test]
  public void Reader_RejectsAFileTooShortForItsHeader() {
    Assert.Throws<InvalidDataException>(() => MsxGl16Reader.FromBytes([64, 0, 64, 0, 1, 2, 3]));
  }

  [Test]
  public void Reader_RejectsAnImpossibleHeader() {
    Assert.Throws<InvalidDataException>(() => MsxGl16Reader.FromBytes([0, 0, 0, 0, 0]));
  }

  [Test]
  public void Screen7_RejectsAnOddHeight() {
    Assert.Throws<ArgumentException>(() => MsxGl16File.FromRawImage(_Bands(8, 3), MsxGl16Mode.Screen7));
  }
}
