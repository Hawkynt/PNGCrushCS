using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Fsh;

namespace FileFormat.Fsh.Tests;

[TestFixture]
public sealed class FshReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FshReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FshReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fsh"));
    Assert.Throws<FileNotFoundException>(() => FshReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FshReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => FshReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16];
    data[0] = (byte)'B';
    data[1] = (byte)'A';
    data[2] = (byte)'D';
    data[3] = (byte)'!';
    Assert.Throws<InvalidDataException>(() => FshReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidArgb8888_ParsesCorrectly() {
    var data = _BuildSingleEntryFsh(FshRecordCode.Argb8888, 2, 1, new byte[2 * 1 * 4]);

    var result = FshReader.FromBytes(data);

    Assert.That(result.Entries.Count, Is.EqualTo(1));
    Assert.That(result.Entries[0].Width, Is.EqualTo(2));
    Assert.That(result.Entries[0].Height, Is.EqualTo(1));
    Assert.That(result.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Argb8888));
    Assert.That(result.Entries[0].PixelData.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb888_ParsesCorrectly() {
    var pixels = new byte[4 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var data = _BuildSingleEntryFsh(FshRecordCode.Rgb888, 4, 2, pixels);

    var result = FshReader.FromBytes(data);

    Assert.That(result.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Rgb888));
    Assert.That(result.Entries[0].Width, Is.EqualTo(4));
    Assert.That(result.Entries[0].Height, Is.EqualTo(2));
    Assert.That(result.Entries[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DirectoryIdIsParsed() {
    var data = _BuildSingleEntryFsh(FshRecordCode.Argb8888, 1, 1, new byte[4]);

    var result = FshReader.FromBytes(data);

    Assert.That(result.DirectoryId, Is.EqualTo("GIMX"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_EntryTagIsParsed() {
    var data = _BuildSingleEntryFsh(FshRecordCode.Argb8888, 1, 1, new byte[4], "tst0");

    var result = FshReader.FromBytes(data);

    Assert.That(result.Entries[0].Tag, Is.EqualTo("tst0"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DirectoryTooLarge_ThrowsInvalidDataException() {
    // Header says 100 entries but file is only 16 bytes
    var data = new byte[16];
    data[0] = (byte)'S';
    data[1] = (byte)'H';
    data[2] = (byte)'P';
    data[3] = (byte)'I';
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 16);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 100);
    Encoding.ASCII.GetBytes("GIMX").CopyTo(data.AsSpan(12));

    Assert.Throws<InvalidDataException>(() => FshReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Indexed8_HasPalette() {
    var palette = new byte[1024];
    for (var i = 0; i < 256; ++i) {
      palette[i * 4] = (byte)i; // B
      palette[i * 4 + 1] = (byte)(255 - i); // G
      palette[i * 4 + 2] = (byte)(i / 2); // R
      palette[i * 4 + 3] = 0xFF; // A
    }

    var indices = new byte[4 * 4];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)(i % 256);

    var data = _BuildIndexed8Fsh(4, 4, palette, indices);

    var result = FshReader.FromBytes(data);

    Assert.That(result.Entries[0].RecordCode, Is.EqualTo(FshRecordCode.Indexed8));
    Assert.That(result.Entries[0].Palette, Is.Not.Null);
    Assert.That(result.Entries[0].Palette!.Length, Is.EqualTo(1024));
    Assert.That(result.Entries[0].PixelData.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildSingleEntryFsh(FshRecordCode.Argb8888, 2, 2, new byte[2 * 2 * 4]);
    using var ms = new MemoryStream(data);

    var result = FshReader.FromStream(ms);

    Assert.That(result.Entries.Count, Is.EqualTo(1));
    Assert.That(result.Entries[0].Width, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CenterCoordinates_Preserved() {
    var data = _BuildSingleEntryFsh(FshRecordCode.Argb8888, 2, 2, new byte[2 * 2 * 4], centerX: 10, centerY: 20);

    var result = FshReader.FromBytes(data);

    Assert.That(result.Entries[0].CenterX, Is.EqualTo(10));
    Assert.That(result.Entries[0].CenterY, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void CalculatePixelDataSize_Argb8888_ReturnsCorrectSize() {
    var size = FshReader._CalculatePixelDataSize(FshRecordCode.Argb8888, 4, 4);
    Assert.That(size, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void CalculatePixelDataSize_Rgb888_ReturnsCorrectSize() {
    var size = FshReader._CalculatePixelDataSize(FshRecordCode.Rgb888, 4, 4);
    Assert.That(size, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void CalculatePixelDataSize_Rgb565_ReturnsCorrectSize() {
    var size = FshReader._CalculatePixelDataSize(FshRecordCode.Rgb565, 4, 4);
    Assert.That(size, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void CalculatePixelDataSize_Indexed8_ReturnsCorrectSize() {
    var size = FshReader._CalculatePixelDataSize(FshRecordCode.Indexed8, 4, 4);
    Assert.That(size, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CalculatePixelDataSize_Dxt1_ReturnsCorrectSize() {
    var size = FshReader._CalculatePixelDataSize(FshRecordCode.Dxt1, 8, 8);
    Assert.That(size, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void CalculatePixelDataSize_Dxt3_ReturnsCorrectSize() {
    var size = FshReader._CalculatePixelDataSize(FshRecordCode.Dxt3, 8, 8);
    Assert.That(size, Is.EqualTo(64));
  }

  private static byte[] _BuildSingleEntryFsh(FshRecordCode code, int width, int height, byte[] pixels, string tag = "img0", int centerX = 0, int centerY = 0) {
    var headerSize = 16;
    var dirEntrySize = 8;
    var recordHeaderSize = 16;
    var totalSize = headerSize + dirEntrySize + recordHeaderSize + pixels.Length;

    var data = new byte[totalSize];
    var span = data.AsSpan();

    // File header
    data[0] = (byte)'S';
    data[1] = (byte)'H';
    data[2] = (byte)'P';
    data[3] = (byte)'I';
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], totalSize);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], 1);
    Encoding.ASCII.GetBytes("GIMX").CopyTo(span[12..]);

    // Directory entry
    Encoding.ASCII.GetBytes(tag.PadRight(4, '\0')[..4]).CopyTo(span[16..]);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], headerSize + dirEntrySize);

    // Record header
    var recOff = headerSize + dirEntrySize;
    data[recOff] = (byte)code;
    BinaryPrimitives.WriteUInt16LittleEndian(span[(recOff + 4)..], (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(recOff + 6)..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(recOff + 8)..], (ushort)centerX);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(recOff + 10)..], (ushort)centerY);

    // Pixel data
    Array.Copy(pixels, 0, data, recOff + recordHeaderSize, pixels.Length);

    return data;
  }

  private static byte[] _BuildIndexed8Fsh(int width, int height, byte[] palette, byte[] indices) {
    var headerSize = 16;
    var dirEntrySize = 8;
    var recordHeaderSize = 16;
    var totalSize = headerSize + dirEntrySize + recordHeaderSize + palette.Length + indices.Length;

    var data = new byte[totalSize];
    var span = data.AsSpan();

    // File header
    data[0] = (byte)'S';
    data[1] = (byte)'H';
    data[2] = (byte)'P';
    data[3] = (byte)'I';
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], totalSize);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], 1);
    Encoding.ASCII.GetBytes("GIMX").CopyTo(span[12..]);

    // Directory entry
    Encoding.ASCII.GetBytes("pal0").CopyTo(span[16..]);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], headerSize + dirEntrySize);

    // Record header
    var recOff = headerSize + dirEntrySize;
    data[recOff] = (byte)FshRecordCode.Indexed8;
    BinaryPrimitives.WriteUInt16LittleEndian(span[(recOff + 4)..], (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(recOff + 6)..], (ushort)height);

    // Palette then pixel data
    var pixelStart = recOff + recordHeaderSize;
    Array.Copy(palette, 0, data, pixelStart, palette.Length);
    Array.Copy(indices, 0, data, pixelStart + palette.Length, indices.Length);

    return data;
  }
}
