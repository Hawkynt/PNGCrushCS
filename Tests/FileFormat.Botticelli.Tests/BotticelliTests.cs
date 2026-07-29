using System;
using System.IO;
using FileFormat.Botticelli;
using FileFormat.Core;

namespace FileFormat.Botticelli.Tests;

[TestFixture]
public sealed class BotticelliTests {

  private static byte[] _Screen(bool multicolor) {
    var data = new byte[BotticelliFile.ScreenFileSize];
    if (multicolor)
      BotticelliFile.MulticolorMarker.CopyTo(data.AsSpan(BotticelliFile.MarkerOffset));

    return data;
  }

  [Test]
  public void Mode_ComesFromTheLengthAndTheMarker() {
    Assert.Multiple(() => {
      Assert.That(BotticelliReader.FromBytes(_Screen(false)).Mode, Is.EqualTo(BotticelliMode.Hires));
      Assert.That(BotticelliReader.FromBytes(_Screen(true)).Mode, Is.EqualTo(BotticelliMode.Multicolor));
      Assert.That(BotticelliReader.FromBytes(new byte[BotticelliFile.LogoFileSize]).Mode, Is.EqualTo(BotticelliMode.Logo));
    });
  }

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => BotticelliReader.FromBytes(new byte[10049]));
      Assert.Throws<InvalidDataException>(() => BotticelliReader.FromBytes(new byte[2051]));
      Assert.Throws<InvalidDataException>(() => BotticelliReader.FromBytes([]));
    });
  }

  [Test]
  public void Dimensions_FollowTheMode() {
    Assert.Multiple(() => {
      var screen = BotticelliReader.FromBytes(_Screen(false));
      Assert.That((screen.Width, screen.Height), Is.EqualTo((320, 200)));

      var logo = BotticelliReader.FromBytes(new byte[BotticelliFile.LogoFileSize]);
      Assert.That((logo.Width, logo.Height), Is.EqualTo((256, 64)));
    });
  }

  [Test]
  public void Hires_TakesForegroundFromOneNibbleAndBackgroundFromTheOther() {
    var data = _Screen(false);
    // Cell 0: luminance byte holds background luminance high, foreground luminance low; the hue
    // byte holds them the other way round. Getting either swapped shows up here.
    data[BotticelliFile.LuminanceOffset] = 0x62;      // background luminance 6, foreground luminance 2
    data[BotticelliFile.HueOffset] = 0x3D;            // foreground hue 3, background hue 13
    data[BotticelliFile.BitmapOffset] = 0b1000_0000;  // pixel 0 set, pixel 1 clear

    var image = BotticelliFile.ToRawImage(BotticelliReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(_IndexAt(image, 0, 0), Is.EqualTo(Commodore16Graphics.ColorIndex(2, 3)), "foreground");
      Assert.That(_IndexAt(image, 1, 0), Is.EqualTo(Commodore16Graphics.ColorIndex(6, 13)), "background");
    });
  }

  [Test]
  public void Multicolor_DrawsTwoPatternsFromTheSharedRegisters() {
    var data = _Screen(true);
    data[BotticelliFile.BackgroundOffset] = 0x41;      // pattern 11
    data[BotticelliFile.BackgroundOffset + 1] = 0x72;  // pattern 00
    data[BotticelliFile.LuminanceOffset] = 0x53;
    data[BotticelliFile.HueOffset] = 0x9A;
    data[BotticelliFile.BitmapOffset] = 0b00_01_10_11;

    var image = BotticelliFile.ToRawImage(BotticelliReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(_IndexAt(image, 0, 0), Is.EqualTo(Commodore16Graphics.ColorIndex(0x72 & 7, 0x72 >> 4)), "pattern 00");
      Assert.That(_IndexAt(image, 2, 0), Is.EqualTo(Commodore16Graphics.ColorIndex(0x53 & 7, 0x9A >> 4)), "pattern 01");
      Assert.That(_IndexAt(image, 4, 0), Is.EqualTo(Commodore16Graphics.ColorIndex(0x53 >> 4, 0x9A & 15)), "pattern 10");
      Assert.That(_IndexAt(image, 6, 0), Is.EqualTo(Commodore16Graphics.ColorIndex(0x41 & 7, 0x41 >> 4)), "pattern 11");
    });
  }

  [Test]
  public void Multicolor_DrawsEveryPixelTwiceWide() {
    var data = _Screen(true);
    data[BotticelliFile.BitmapOffset] = 0b00_11_00_11;

    var image = BotticelliFile.ToRawImage(BotticelliReader.FromBytes(data));

    for (var x = 0; x < 8; x += 2)
      Assert.That(_IndexAt(image, x + 1, 0), Is.EqualTo(_IndexAt(image, x, 0)), $"pixel pair at x={x}");
  }

  [Test]
  public void Rows_WithinACellAreConsecutiveBytes() {
    var data = _Screen(false);
    data[BotticelliFile.LuminanceOffset] = 0x01;
    data[BotticelliFile.HueOffset] = 0x10;
    // The eight bytes of cell 0 are its eight pixel rows; a decoder that treated them as eight
    // cells of one row would light the wrong pixels.
    for (var row = 0; row < 8; ++row)
      data[BotticelliFile.BitmapOffset + row] = (byte)(row % 2 == 0 ? 0xFF : 0x00);

    var image = BotticelliFile.ToRawImage(BotticelliReader.FromBytes(data));
    var lit = Commodore16Graphics.ColorIndex(1, 1);

    for (var y = 0; y < 8; ++y)
      Assert.That(_IndexAt(image, 0, y), Is.EqualTo(y % 2 == 0 ? lit : 0), $"row {y}");
  }

  [Test]
  public void Logo_IsStoredColumnOfCellsFirst() {
    var data = new byte[BotticelliFile.LogoFileSize];
    // Byte 2 is x 0..7 of row 0; byte 2+64 starts the next column of cells, not the next row.
    data[BotticelliFile.LogoBitmapOffset] = 0b11_00_00_00;
    data[BotticelliFile.LogoBitmapOffset + 64] = 0b11_00_00_00;

    var image = BotticelliFile.ToRawImage(BotticelliReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(_IndexAt(image, 0, 0), Is.EqualTo(BotticelliFile.LogoColors[3]));
      Assert.That(_IndexAt(image, 8, 0), Is.EqualTo(BotticelliFile.LogoColors[3]));
      Assert.That(_IndexAt(image, 2, 0), Is.EqualTo(BotticelliFile.LogoColors[0]));
    });
  }

  [Test]
  public void Palette_IsTheFullLuminanceByHueTable() {
    var image = BotticelliFile.ToRawImage(BotticelliReader.FromBytes(_Screen(false)));

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(Commodore16Graphics.ColorCount));
      // Every luminance of hue 0 is the same black — the TED's one genuine duplicate.
      for (var luminance = 0; luminance < 8; ++luminance) {
        var i = Commodore16Graphics.ColorIndex(luminance, 0) * 3;
        Assert.That(image.Palette![i..(i + 3)], Is.EqualTo(new byte[] { 3, 3, 3 }), $"luminance {luminance}, hue 0");
      }
    });
  }

  private static int _IndexAt(RawImage image, int x, int y) => image.PixelData[y * image.Width + x];
}
