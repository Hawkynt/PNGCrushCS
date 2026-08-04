using System;
using System.IO;
using FileFormat.HereticM8;
using FileFormat.Core;

namespace FileFormat.HereticM8.Tests;

/// <summary>
/// What a Heretic II texture is.
/// </summary>
/// <remarks>
/// These used to hand the reader a buffer with a size written into its first bytes and assert only
/// that a positive size came back. A real file states a version of 2, a name, and then sixteen widths,
/// heights and offsets — one set per mipmap level.
/// </remarks>
[TestFixture]
public class HereticM8ReaderTests {

  /// <summary>Builds a texture of one mipmap level, filled with a single palette index.</summary>
  private static byte[] _BuildValidFile(int width, int height, byte fill) {
    var pixels = HereticM8File.PaletteOffset + 768 + 12;
    var data = new byte[pixels + width * height];

    void Long(int at, int value) {
      data[at] = (byte)value;
      data[at + 1] = (byte)(value >> 8);
      data[at + 2] = (byte)(value >> 16);
      data[at + 3] = (byte)(value >> 24);
    }

    Long(0, HereticM8File.Version);
    Long(HereticM8File.WidthsOffset, width);
    Long(HereticM8File.WidthsOffset + HereticM8File.Levels * 4, height);
    Long(HereticM8File.WidthsOffset + HereticM8File.Levels * 8, pixels);

    // A palette whose entry one is a colour worth recognising.
    data[HereticM8File.PaletteOffset + 3] = 0x11;
    data[HereticM8File.PaletteOffset + 4] = 0x22;
    data[HereticM8File.PaletteOffset + 5] = 0x33;

    for (var i = 0; i < width * height; ++i)
      data[pixels + i] = fill;

    return data;
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => HereticM8Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HereticM8Reader.FromBytes(new byte[64]));

  [Test]
  public void FromBytes_WrongVersion_ThrowsInvalidDataException() {
    var data = _BuildValidFile(8, 8, 0);
    data[0] = 9;

    Assert.Throws<InvalidDataException>(() => HereticM8Reader.FromBytes(data));
  }

  [Test]
  public void FromBytes_TakesTheSizeOfTheFirstMipmapLevel() {
    var result = HereticM8Reader.FromBytes(_BuildValidFile(256, 256, 1));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(256));
      Assert.That(result.Height, Is.EqualTo(256));
      Assert.That(result.PixelData.Length, Is.EqualTo(256 * 256));
      Assert.That(result.PixelData[0], Is.EqualTo(1));
    });
  }

  [Test]
  public void FromBytes_ReadsThePaletteAfterTheTables() {
    var result = HereticM8Reader.FromBytes(_BuildValidFile(8, 8, 1));

    Assert.That(result.Palette[3..6], Is.EqualTo(new byte[] { 0x11, 0x22, 0x33 }));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HereticM8Reader.FromStream(null!));

  [Test]
  public void ToRawImage_IsIndexedWithAll256Colours() {
    var raw = HereticM8File.ToRawImage(HereticM8Reader.FromBytes(_BuildValidFile(8, 8, 1)));

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(256));
    });
  }
}
