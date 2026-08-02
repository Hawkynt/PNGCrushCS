using System;
using FileFormat.Core;
using FileFormat.Uimg;

namespace FileFormat.Uimg.Tests;

/// <summary>
/// The header names which of three kinds of palette follows, and only one of them is the Atari's.
/// </summary>
/// <remarks>
/// All three were read as the Atari's, which puts every channel of a plain twelve-bit palette in the
/// wrong place. A real 1280 by 1024 picture came out green where it should have been blue: the words
/// 0123, 025a, 047b and 089c are the colours 112233, 2255aa, 4477bb and 8899cc, and were arriving as
/// something else entirely.
/// <para/>
/// With the kind honoured that picture matches RECOIL on all 1310720 of its pixels.
/// </remarks>
[TestFixture]
public sealed class UimgPaletteKindTests {

  /// <summary>Builds a two-plane picture with the given palette kind and palette words.</summary>
  private static byte[] _Picture(int paletteKind, params ushort[] entries) {
    const int width = 16, height = 1, depth = 2;
    var paletteBytes = entries.Length * 2;
    var planeBytes = width * height / 8 * depth;
    var data = new byte[14 + paletteBytes + planeBytes];

    "UIMG"u8.CopyTo(data);
    data[4] = 1;
    data[5] = 1;
    data[7] = (byte)paletteKind;
    data[8] = depth;
    data[9] = 0;                 // bitplanes
    data[10] = 0; data[11] = width;
    data[12] = 0; data[13] = height;

    for (var i = 0; i < entries.Length; ++i) {
      data[14 + i * 2] = (byte)(entries[i] >> 8);
      data[14 + i * 2 + 1] = (byte)entries[i];
    }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void ATwelveBitPaletteKeepsItsChannelsWhereTheyWereWritten() {
    var image = UimgFile.ToRawImage(UimgReader.FromBytes(_Picture(2, 0x0123, 0x025A, 0x047B, 0x089C)));

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(0x11));
      Assert.That(image.Palette![1], Is.EqualTo(0x22));
      Assert.That(image.Palette![2], Is.EqualTo(0x33));
      Assert.That(image.Palette![3], Is.EqualTo(0x22));
      Assert.That(image.Palette![4], Is.EqualTo(0x55));
      Assert.That(image.Palette![5], Is.EqualTo(0xAA));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachNibbleFillsItsWholeByte() {
    // 0xF must reach 255 and 0x0 stay at nought, or white stops being white.
    var image = UimgFile.ToRawImage(UimgReader.FromBytes(_Picture(2, 0x0FFF, 0x0000, 0x0F00, 0x000F)));

    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(255));
      Assert.That(image.Palette![1], Is.EqualTo(255));
      Assert.That(image.Palette![2], Is.EqualTo(255));
      Assert.That(image.Palette![3], Is.Zero);
      Assert.That(image.Palette![6], Is.EqualTo(255), "red alone");
      Assert.That(image.Palette![7], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheAtarisOwnPaletteIsStillReadAsTheAtarisOwn() {
    // Kind 1 keeps its old reading, which is not the same as the twelve-bit one.
    var atari = UimgFile.ToRawImage(UimgReader.FromBytes(_Picture(1, 0x0123, 0x025A, 0x047B, 0x089C)));
    var plain = UimgFile.ToRawImage(UimgReader.FromBytes(_Picture(2, 0x0123, 0x025A, 0x047B, 0x089C)));

    Assert.That(atari.Palette, Is.Not.EqualTo(plain.Palette), "the two kinds are not the same palette");
  }
}
