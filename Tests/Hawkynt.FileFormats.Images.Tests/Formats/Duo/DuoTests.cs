using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Duo;

namespace FileFormat.Duo.Tests;

[TestFixture]
public sealed class DuoTests {

  private static byte[] _Empty() => new byte[DuoFile.FileSize];

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => DuoReader.FromBytes(new byte[DuoFile.FileSize - 1]));
      Assert.Throws<InvalidDataException>(() => DuoReader.FromBytes(new byte[DuoFile.FileSize + 1]));
    });
  }

  [Test]
  public void Dimensions_ReachPastTheNormalBorders() {
    var image = DuoFile.ToRawImage(DuoReader.FromBytes(_Empty()));

    Assert.Multiple(() => {
      Assert.That((image.Width, image.Height), Is.EqualTo((416, 273)));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  public void TheTwoFramesAreAveraged_NotJustTheFirst() {
    var data = _Empty();
    // Entry 0 black, entry 1 white, both in STE form so the palette reads as four-bit.
    data[1] = 0x00;
    data[2] = 0x0F; data[3] = 0xFF;
    // Light pixel 0 in the second frame only: black in one field, white in the other.
    data[DuoFile.SecondFrameOffset] = 0x80;

    var image = DuoFile.ToRawImage(DuoReader.FromBytes(data));

    // Averaged, and rounded down, so mid-grey lands one below centre.
    Assert.That(image.PixelData[0], Is.EqualTo(127));
  }

  [Test]
  public void AnStPaletteIsReadAsThreeBits_AndAnSteOneAsFour() {
    Assert.Multiple(() => {
      // An entry using only the ST levels sets none of the bits that mark an STE palette.
      Assert.That(AtariStGraphics.IsStePalette(new byte[] { 0x07, 0x77 }, 0, 1), Is.False);
      // Bit 3 of the red nibble is one an ST would never set.
      Assert.That(AtariStGraphics.IsStePalette(new byte[] { 0x0F, 0x77 }, 0, 1), Is.True);
    });
  }

  [Test]
  public void SteChannels_AreRotatedNotExtended() {
    // Value 12 in STE form is 0b1100: low bit at the top, so it means 0b1001 = 9 -> 153.
    var rgb = AtariStGraphics.ReadPalette(new byte[] { 0x0C, 0x00 }, 0, 1);

    Assert.That(rgb[0], Is.EqualTo(ChannelScaling.Expand4(9)));
  }
}
