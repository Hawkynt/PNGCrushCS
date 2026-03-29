using System;
using System.IO;
using FileFormat.ColoRix;

namespace FileFormat.ColoRix.Tests;

[TestFixture]
public sealed class ColoRixReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ColoRixReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ColoRixReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rix"));
    Assert.Throws<FileNotFoundException>(() => ColoRixReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ColoRixReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => ColoRixReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[ColoRixFile.HeaderSize + ColoRixFile.PaletteSize];
    bad[0] = (byte)'N';
    bad[1] = (byte)'O';
    bad[2] = (byte)'P';
    bad[3] = (byte)'E';
    Assert.Throws<InvalidDataException>(() => ColoRixReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUncompressed_ParsesCorrectly() {
    const int width = 4;
    const int height = 3;
    var data = _BuildMinimalColoRix(width, height, ColoRixCompression.None);
    var result = ColoRixReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Palette.Length, Is.EqualTo(ColoRixFile.PaletteSize));
    Assert.That(result.PixelData.Length, Is.EqualTo(width * height));
    Assert.That(result.StorageType, Is.EqualTo(ColoRixCompression.None));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteValues_ReadCorrectly() {
    const int width = 2;
    const int height = 2;
    var data = _BuildMinimalColoRix(width, height, ColoRixCompression.None);

    var paletteStart = ColoRixFile.HeaderSize;
    data[paletteStart] = 63;
    data[paletteStart + 1] = 32;
    data[paletteStart + 2] = 0;

    var result = ColoRixReader.FromBytes(data);

    Assert.That(result.Palette[0], Is.EqualTo(63));
    Assert.That(result.Palette[1], Is.EqualTo(32));
    Assert.That(result.Palette[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_ReadCorrectly() {
    const int width = 2;
    const int height = 2;
    var data = _BuildMinimalColoRix(width, height, ColoRixCompression.None);

    var pixelStart = ColoRixFile.HeaderSize + ColoRixFile.PaletteSize;
    data[pixelStart] = 10;
    data[pixelStart + 1] = 20;
    data[pixelStart + 2] = 30;
    data[pixelStart + 3] = 40;

    var result = ColoRixReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(10));
    Assert.That(result.PixelData[1], Is.EqualTo(20));
    Assert.That(result.PixelData[2], Is.EqualTo(30));
    Assert.That(result.PixelData[3], Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    const int width = 3;
    const int height = 2;
    var data = _BuildMinimalColoRix(width, height, ColoRixCompression.None);

    using var ms = new MemoryStream(data);
    var result = ColoRixReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
  }

  private static byte[] _BuildMinimalColoRix(int width, int height, ColoRixCompression compression) {
    var pixelCount = width * height;
    var fileSize = ColoRixFile.HeaderSize + ColoRixFile.PaletteSize + pixelCount;
    var data = new byte[fileSize];

    data[0] = (byte)'R';
    data[1] = (byte)'I';
    data[2] = (byte)'X';
    data[3] = (byte)'3';

    var storedWidth = (ushort)(width - 1);
    var storedHeight = (ushort)(height - 1);
    data[4] = (byte)(storedWidth & 0xFF);
    data[5] = (byte)((storedWidth >> 8) & 0xFF);
    data[6] = (byte)(storedHeight & 0xFF);
    data[7] = (byte)((storedHeight >> 8) & 0xFF);

    data[8] = ColoRixFile.VgaPaletteType;
    data[9] = (byte)compression;

    return data;
  }
}
