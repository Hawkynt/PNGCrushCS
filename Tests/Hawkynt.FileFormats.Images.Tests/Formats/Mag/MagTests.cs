using System;
using System.IO;
using FileFormat.Mag;
using FileFormat.Core;

namespace FileFormat.Mag.Tests;

/// <summary>
/// What a MAKIchan Graphics file is.
/// </summary>
/// <remarks>
/// These used to hand the reader 32 bytes of zeros and assert that whatever came back had a positive
/// size, and to round-trip through a writer that wrote a header no MAG has. The format states MAKI02,
/// carries a comment, a 32-byte header, a palette and three compressed streams; a file of zeros is
/// none of that.
/// </remarks>
[TestFixture]
public class MagReaderTests {

  /// <summary>
  /// Builds the smallest whole picture: eight bytes of signature, a comment, a header, a palette and
  /// three streams. Every unit past the first of a row is coded as a copy of the one to its left, so
  /// the pixel stream carries two bytes a row and everything else repeats them.
  /// </summary>
  private static byte[] _BuildValidFile(int width, int height) {
    var bytesPerRow = width / 2;
    var flagsPerRow = bytesPerRow / 4;

    using var ms = new MemoryStream();
    foreach (var b in MagFile.Magic)
      ms.WriteByte(b);
    ms.WriteByte(0x1A);

    var block = new byte[MagFile.HeaderSize];
    block[3] = 0;                                                   // sixteen colours, no doubling
    block[8] = (byte)(width - 1);
    block[9] = (byte)((width - 1) >> 8);
    block[10] = (byte)(height - 1);
    block[11] = (byte)((height - 1) >> 8);

    var flagABytes = (flagsPerRow * height + 7) / 8;
    var flagAAt = MagFile.HeaderSize + 16 * 3;
    var flagBAt = flagAAt + flagABytes;
    var pixelsAt = flagBAt + flagsPerRow;

    void Long(int at, int value) {
      block[at] = (byte)value;
      block[at + 1] = (byte)(value >> 8);
      block[at + 2] = (byte)(value >> 16);
      block[at + 3] = (byte)(value >> 24);
    }

    Long(12, flagAAt);
    Long(16, flagBAt);
    Long(24, pixelsAt);
    ms.Write(block);

    // A palette of sixteen: green, red, blue.
    for (var i = 0; i < 16; ++i) {
      ms.WriteByte((byte)(i * 16));
      ms.WriteByte((byte)(255 - i * 16));
      ms.WriteByte((byte)(i * 8));
    }

    // Only the first row's flags are stated; every later row repeats them, which costs no bytes.
    var flagA = new byte[flagABytes];
    for (var i = 0; i < flagsPerRow; ++i)
      flagA[i >> 3] |= (byte)(0x80 >> (i & 7));
    ms.Write(flagA);

    // Nought for the first unit of the row, then "copy the unit to my left" for all the rest.
    var flagB = new byte[flagsPerRow];
    flagB[0] = 0x01;
    for (var i = 1; i < flagsPerRow; ++i)
      flagB[i] = 0x11;
    ms.Write(flagB);

    // The first unit of every row keeps a code of nought, so each row takes two bytes of pixels.
    for (var y = 0; y < height; ++y) {
      ms.WriteByte(0x22);
      ms.WriteByte(0x22);
    }

    return ms.ToArray();
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => MagReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MagReader.FromBytes(new byte[4]));

  [Test]
  public void FromBytes_WithoutTheSignature_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MagReader.FromBytes(new byte[64]));

  [Test]
  public void FromBytes_TakesItsSizeFromTheDrawnRegion() {
    var result = MagReader.FromBytes(_BuildValidFile(64, 4));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(64));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PaletteCount, Is.EqualTo(16));
      Assert.That(result.PixelData.Length, Is.EqualTo(64 * 4));
    });
  }

  [Test]
  public void FromBytes_ACopyCodeRepeatsTheUnitItNames() {
    // Every unit past the first copies the one to its left, so the whole picture is the two bytes the
    // pixel stream carries — 0x22 0x22, which is index 2 in every nibble.
    var result = MagReader.FromBytes(_BuildValidFile(64, 4));

    Assert.That(result.PixelData, Is.All.EqualTo(2));
  }

  [Test]
  public void FromBytes_ReadsThePaletteGreenFirst() {
    var result = MagReader.FromBytes(_BuildValidFile(64, 4));

    // Entry one was written green 16, red 239, blue 8, and only the top nibble of each is real — so
    // it comes back as red, green, blue with each nibble repeated: 0xEE, 0x11, 0x00.
    Assert.That(result.Palette[3..6], Is.EqualTo(new byte[] { 238, 17, 0 }));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MagReader.FromStream(null!));

  [Test]
  public void ToRawImage_IsIndexedWithTheFilesPalette() {
    var raw = MagFile.ToRawImage(MagReader.FromBytes(_BuildValidFile(64, 4)));

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(16));
    });
  }
}
