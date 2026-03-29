using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.AutodeskCel;

namespace FileFormat.AutodeskCel.Tests;

[TestFixture]
public sealed class AutodeskCelReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AutodeskCelReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AutodeskCelReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cel"));
    Assert.Throws<FileNotFoundException>(() => AutodeskCelReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AutodeskCelReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => AutodeskCelReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[AutodeskCelFile.HeaderSize + 4];
    BinaryPrimitives.WriteUInt16LittleEndian(data, 0x0000);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 8);

    Assert.Throws<InvalidDataException>(() => AutodeskCelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bpp_ParsesDimensions() {
    var bytes = _BuildMinimalCel(4, 3, 0, 0);
    var result = AutodeskCelReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bpp_ParsesOffsets() {
    var bytes = _BuildMinimalCel(4, 3, 10, 20);
    var result = AutodeskCelReader.FromBytes(bytes);

    Assert.That(result.XOffset, Is.EqualTo(10));
    Assert.That(result.YOffset, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bpp_ParsesBitsPerPixel() {
    var bytes = _BuildMinimalCel(4, 3, 0, 0);
    var result = AutodeskCelReader.FromBytes(bytes);

    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bpp_ParsesPixelData() {
    var bytes = _BuildMinimalCel(2, 2, 0, 0);
    var result = AutodeskCelReader.FromBytes(bytes);

    Assert.That(result.PixelData.Length, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithPalette_ExtractsPalette() {
    var file = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      PixelData = [0, 1, 2, 3],
      Palette = _BuildTestPalette(),
    };
    var bytes = AutodeskCelWriter.ToBytes(file);
    var result = AutodeskCelReader.FromBytes(bytes);

    Assert.That(result.Palette.Length, Is.EqualTo(AutodeskCelFile.PaletteSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithPalette_PaletteValuesScaledCorrectly() {
    var palette = new byte[AutodeskCelFile.PaletteSize];
    palette[0] = 252;
    palette[1] = 128;
    palette[2] = 0;

    var file = new AutodeskCelFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = palette,
    };
    var bytes = AutodeskCelWriter.ToBytes(file);
    var result = AutodeskCelReader.FromBytes(bytes);

    // 252 / 4 = 63 (stored) -> 63 * 4 = 252
    Assert.That(result.Palette[0], Is.EqualTo(252));
    // 128 / 4 = 32 (stored) -> 32 * 4 = 128
    Assert.That(result.Palette[1], Is.EqualTo(128));
    // 0 / 4 = 0 (stored) -> 0 * 4 = 0
    Assert.That(result.Palette[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutPalette_UsesDefaultGrayscale() {
    // Build a file without appended palette (header + pixels only)
    var width = 2;
    var height = 2;
    var pixelDataSize = width * height;
    var data = new byte[AutodeskCelFile.HeaderSize + pixelDataSize];
    var span = data.AsSpan();
    BinaryPrimitives.WriteUInt16LittleEndian(span, AutodeskCelFile.Magic);
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(span[10..], 8);

    var result = AutodeskCelReader.FromBytes(data);

    // Grayscale palette: entry i = (i, i, i)
    Assert.That(result.Palette[0], Is.EqualTo(0));
    Assert.That(result.Palette[1], Is.EqualTo(0));
    Assert.That(result.Palette[2], Is.EqualTo(0));
    Assert.That(result.Palette[3], Is.EqualTo(1));
    Assert.That(result.Palette[4], Is.EqualTo(1));
    Assert.That(result.Palette[5], Is.EqualTo(1));
    Assert.That(result.Palette[765], Is.EqualTo(255));
    Assert.That(result.Palette[766], Is.EqualTo(255));
    Assert.That(result.Palette[767], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var bytes = _BuildMinimalCel(4, 2, 0, 0);
    using var ms = new MemoryStream(bytes);
    var result = AutodeskCelReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[AutodeskCelFile.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data, AutodeskCelFile.Magic);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 0); // width = 0
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 8);

    Assert.Throws<InvalidDataException>(() => AutodeskCelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[AutodeskCelFile.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data, AutodeskCelFile.Magic);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0); // height = 0
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 8);

    Assert.Throws<InvalidDataException>(() => AutodeskCelReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    var data = new byte[AutodeskCelFile.HeaderSize + 2]; // not enough for 4x3 pixels
    BinaryPrimitives.WriteUInt16LittleEndian(data, AutodeskCelFile.Magic);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 4);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 8);

    Assert.Throws<InvalidDataException>(() => AutodeskCelReader.FromBytes(data));
  }

  private static byte[] _BuildTestPalette() {
    var palette = new byte[AutodeskCelFile.PaletteSize];
    for (var i = 0; i < AutodeskCelFile.PaletteEntryCount; ++i) {
      palette[i * 3] = (byte)(i % 64 * 4);
      palette[i * 3 + 1] = (byte)((255 - i) / 4 * 4);
      palette[i * 3 + 2] = (byte)(i / 2 / 4 * 4);
    }
    return palette;
  }

  private static byte[] _BuildMinimalCel(int width, int height, int xOffset, int yOffset) {
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new AutodeskCelFile {
      Width = width,
      Height = height,
      XOffset = xOffset,
      YOffset = yOffset,
      PixelData = pixelData,
    };
    return AutodeskCelWriter.ToBytes(file);
  }
}
