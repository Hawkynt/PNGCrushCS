using System;
using FileFormat.Core;
using FileFormat.MsxScreen5;

namespace FileFormat.MsxScreen5.Tests;

/// <summary>
/// Where a Screen 5 picture keeps its palette, and what happens when it keeps none.
/// </summary>
/// <remarks>
/// The palette is the last thing in the file, not the thirty-two bytes that follow the pixels.
/// Screen 5 shows 212 lines of a page that holds more, so a saved page runs past what is drawn and
/// the palette sits after all of it; reading at the end of the drawn part picks up picture data and
/// paints the whole thing in it.
/// <para/>
/// A file need not carry one at all, and many do not. Handing back no palette left an indexed
/// picture with no colours to be drawn in, which threw rather than decoded.
/// <para/>
/// Checked against RECOIL on two real files, one of each kind: both match on every pixel.
/// </remarks>
[TestFixture]
public sealed class MsxScreen5PaletteTests {

  private const int _PIXELS = 256 * 212 / 2;

  private static byte[] _File(int trailingBytes, byte[]? palette) {
    var data = new byte[7 + _PIXELS + trailingBytes + (palette?.Length ?? 0)];
    data[0] = 0xFE;
    for (var i = 0; i < _PIXELS; ++i)
      data[7 + i] = 0x01;

    palette?.CopyTo(data, data.Length - palette.Length);
    return data;
  }

  /// <summary>A palette naming entry 1 pure green: red and blue nil, green at full.</summary>
  private static byte[] _GreenAtOne() {
    var palette = new byte[32];
    palette[2] = 0x00; // entry 1: red and blue both nil
    palette[3] = 0x07; // entry 1: green at full
    return palette;
  }

  [Test]
  [Category("Unit")]
  public void ThePaletteIsTakenFromTheEndOfTheFile() {
    // Three thousand bytes of page sit between the drawn pixels and the palette, as in a real file.
    var image = MsxScreen5File.ToRawImage(MsxScreen5Reader.FromBytes(_File(3200, _GreenAtOne())));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![3], Is.Zero, "entry one is pure green");
      Assert.That(image.Palette![4], Is.EqualTo(255));
      Assert.That(image.Palette![5], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void APaletteDirectlyAfterThePixelsIsStillFound() {
    var image = MsxScreen5File.ToRawImage(MsxScreen5Reader.FromBytes(_File(0, _GreenAtOne())));

    Assert.That(image.Palette![4], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoPaletteFallsBackToTheMachinesOwn() {
    var image = MsxScreen5File.ToRawImage(MsxScreen5Reader.FromBytes(_File(0, null)));

    Assert.That(image.Palette, Is.Not.Null, "an indexed picture needs colours to be drawn in");
    Assert.That(image.PaletteCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void AChannelIsWidenedByRepeatingItsBitsAndNotByDividing() {
    // Three bits of value four is 146, which repeating the bits gives; dividing by seven gives 145.
    var palette = new byte[32];
    palette[2] = 0x40; // entry 1: red at four
    var image = MsxScreen5File.ToRawImage(MsxScreen5Reader.FromBytes(_File(0, palette)));

    Assert.That(image.Palette![3], Is.EqualTo(146));
  }
}
