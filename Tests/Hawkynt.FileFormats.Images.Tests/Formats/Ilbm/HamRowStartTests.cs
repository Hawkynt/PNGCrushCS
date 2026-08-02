using System;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

/// <summary>
/// What colour a Hold-And-Modify scanline starts from.
/// </summary>
/// <remarks>
/// It starts from the background colour, which is the palette's first entry, and not from black.
/// The two are usually close enough to pass unnoticed: the difference shows in the first one or two
/// pixels of every row and nowhere else, because the holding carries the border colour in until
/// something modifies each channel in turn.
/// <para/>
/// On a real 640 by 512 picture that was 1024 pixels out of 327680 — a hundredth of a per cent, and
/// the whole of the difference from RECOIL. With it corrected the HAM and HAM8 samples match on
/// every pixel, and the HAM6 one does too once RECOIL's doubling of the width is undone.
/// </remarks>
[TestFixture]
public sealed class HamRowStartTests {

  /// <summary>A palette whose first entry is a colour nobody would mistake for black.</summary>
  private static byte[] _Palette() {
    var palette = new byte[16 * 3];
    palette[0] = 10;
    palette[1] = 20;
    palette[2] = 30;
    return palette;
  }

  [Test]
  [Category("Unit")]
  public void ARowStartsFromTheBackgroundColour() {
    // Control 1 modifies blue only, so red and green must still hold the background.
    byte[] pixels = [0b01_0000];
    var rgb = HamDecoder.Decode(pixels, _Palette(), 1, 1, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(10), "red is held from the background");
      Assert.That(rgb[1], Is.EqualTo(20), "green likewise");
      Assert.That(rgb[2], Is.Zero, "blue is the one that was modified");
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryRowStartsAfreshAndNotWhereTheLastLeftOff() {
    // Two rows of one pixel: each modifies blue, so each must hold the background's red and green.
    byte[] pixels = [0b01_1111, 0b01_0000];
    var rgb = HamDecoder.Decode(pixels, _Palette(), 1, 2, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(10));
      Assert.That(rgb[3], Is.EqualTo(10), "the second row holds the background too");
      Assert.That(rgb[4], Is.EqualTo(20));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChoosingAPaletteEntryStillOverridesEverything() {
    // Control 0 takes the colour whole, so nothing is held.
    byte[] pixels = [0b00_0001];
    var palette = _Palette();
    palette[3] = 200;
    palette[4] = 100;
    palette[5] = 50;

    var rgb = HamDecoder.Decode(pixels, palette, 1, 1, 6);

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(200));
      Assert.That(rgb[1], Is.EqualTo(100));
      Assert.That(rgb[2], Is.EqualTo(50));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyPaletteLeavesTheRowStartingFromBlack() {
    byte[] pixels = [0b01_0000];
    var rgb = HamDecoder.Decode(pixels, [], 1, 1, 6);

    Assert.That(rgb[0], Is.Zero);
  }
}
