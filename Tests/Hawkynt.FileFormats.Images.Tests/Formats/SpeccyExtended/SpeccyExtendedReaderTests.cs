using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SpeccyExtended;

namespace FileFormat.SpeccyExtended.Tests;

/// <summary>
/// What a Speccy eXtended Graphics picture is.
/// </summary>
/// <remarks>
/// These used to build a 7684-byte ZX Spectrum screen with an extra attribute plane and assert it
/// came back. An SXG is a ZX Evolution picture: it states its own size, carries its own sixteen
/// colours and holds four bits a pixel. The samples are 38926 and 25102 bytes at 320 by 240 and 256
/// by 192, and both were refused — the reason given being that the magic was "SX", because the
/// signature sits at offset one after a leading 0x7F and the check read from nought.
/// </remarks>
[TestFixture]
public sealed class SpeccyExtendedReaderTests {

  /// <summary>Builds a picture of the real shape, with a palette whose entries are told apart.</summary>
  private static byte[] _BuildValidFile(int width, int height) {
    var data = new byte[SpeccyExtendedFile.PixelOffset + (width * height + 1) / 2];
    data[0] = 0x7F;
    data[1] = (byte)'S';
    data[2] = (byte)'X';
    data[3] = (byte)'G';
    data[4] = 3;

    data[SpeccyExtendedFile.WidthOffset] = (byte)width;
    data[SpeccyExtendedFile.WidthOffset + 1] = (byte)(width >> 8);
    data[SpeccyExtendedFile.WidthOffset + 2] = (byte)height;
    data[SpeccyExtendedFile.WidthOffset + 3] = (byte)(height >> 8);

    // Entry one: red 3, green 14, blue 23 — the values the first sample's entry one holds.
    var value = (3 << 10) | (14 << 5) | 23;
    data[SpeccyExtendedFile.PaletteOffset + 2] = (byte)value;
    data[SpeccyExtendedFile.PaletteOffset + 3] = (byte)(value >> 8);

    // The first byte of the picture: index 1 then index 2.
    data[SpeccyExtendedFile.PixelOffset] = 0x12;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SpeccyExtendedReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sxg"));

    Assert.Throws<FileNotFoundException>(() => SpeccyExtendedReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheLeadingByte_ThrowsInvalidDataException() {
    // "SXG" at offset nought is not the signature; it starts one byte in.
    var data = _BuildValidFile(64, 8);
    data[0] = (byte)'S';
    data[1] = (byte)'X';
    data[2] = (byte)'G';

    Assert.Throws<InvalidDataException>(() => SpeccyExtendedReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesItsSizeFromTheHeader() {
    var result = SpeccyExtendedReader.FromBytes(_BuildValidFile(320, 240));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(240));
      Assert.That(result.PixelData, Has.Length.EqualTo(320 * 240));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsFourBitsAPixelHighNibbleFirst() {
    var result = SpeccyExtendedReader.FromBytes(_BuildValidFile(64, 8));

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(1));
      Assert.That(result.PixelData[1], Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsFiveBitsAChannelWithRedHighest() {
    // A channel of 8 is drawn as 85 and one of 16 as 170, so the scale is 24 rather than the 31
    // five bits could hold. Established by setting one entry to a single bit at a time.
    var result = SpeccyExtendedReader.FromBytes(_BuildValidFile(64, 8));

    Assert.That(result.Palette[3..6], Is.EqualTo(new byte[] { 31, 148, 244 }));
  }

  [Test]
  [Category("Unit")]
  public void DecodeColor_AnythingPastFullScaleIsWhite() {
    // Twenty-four is full; the five bits reach thirty-one and the rest clamps.
    var (r, g, b) = SpeccyExtendedFile.DecodeColor((31 << 10) | (31 << 5) | 31);

    Assert.Multiple(() => {
      Assert.That(r, Is.EqualTo(255));
      Assert.That(g, Is.EqualTo(255));
      Assert.That(b, Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmallForItsStatedSize_ThrowsInvalidDataException() {
    var data = _BuildValidFile(320, 240);

    Assert.Throws<InvalidDataException>(() => SpeccyExtendedReader.FromBytes(data[..1000]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SizePaletteAndPixelsAllComeBack() {
    var original = SpeccyExtendedReader.FromBytes(_BuildValidFile(64, 8));

    var restored = SpeccyExtendedReader.FromBytes(SpeccyExtendedWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette[3..6], Is.EqualTo(original.Palette[3..6]));
    });
  }
}
