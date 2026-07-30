using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Xcursor;

namespace FileFormat.Xcursor.Tests;

[TestFixture]
public sealed class XcursorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcursorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcursorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xcur"));
    Assert.Throws<FileNotFoundException>(() => XcursorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcursorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => XcursorReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16];
    data[0] = (byte)'X';
    data[1] = (byte)'X';
    data[2] = (byte)'X';
    data[3] = (byte)'X';
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x00010000);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0);
    Assert.Throws<InvalidDataException>(() => XcursorReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoImageChunk_ThrowsInvalidDataException() {
    var data = _BuildMinimalFile(2, 2, 0, 0, 32, 0, includeImageChunk: false);
    Assert.Throws<InvalidDataException>(() => XcursorReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var data = _BuildMinimalFile(4, 3, 1, 2, 32, 50);

    var result = XcursorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesHotspot() {
    var data = _BuildMinimalFile(2, 2, 5, 7, 24, 0);

    var result = XcursorReader.FromBytes(data);

    Assert.That(result.XHot, Is.EqualTo(5));
    Assert.That(result.YHot, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesNominalSize() {
    var data = _BuildMinimalFile(2, 2, 0, 0, 48, 0);

    var result = XcursorReader.FromBytes(data);

    Assert.That(result.NominalSize, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDelay() {
    var data = _BuildMinimalFile(1, 1, 0, 0, 32, 100);

    var result = XcursorReader.FromBytes(data);

    Assert.That(result.Delay, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_PixelDataPreserved() {
    var pixelData = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00 };
    var data = _BuildMinimalFileWithPixels(2, 2, 0, 0, 32, 0, pixelData);

    var result = XcursorReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var data = _BuildMinimalFile(3, 2, 1, 0, 24, 75);

    using var ms = new MemoryStream(data);
    var result = XcursorReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.XHot, Is.EqualTo(1));
    Assert.That(result.Delay, Is.EqualTo(75));
  }

  private static byte[] _BuildMinimalFile(int width, int height, int xhot, int yhot, int nominalSize, int delay, bool includeImageChunk = true) {
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    return _BuildMinimalFileWithPixels(width, height, xhot, yhot, nominalSize, delay, pixelData, includeImageChunk);
  }

  private static byte[] _BuildMinimalFileWithPixels(int width, int height, int xhot, int yhot, int nominalSize, int delay, byte[] pixelData, bool includeImageChunk = true) {
    const int fileHeaderSize = 16;
    const int tocEntrySize = 12;
    const int imageChunkHeaderSize = 36;

    if (!includeImageChunk) {
      var noChunkData = new byte[fileHeaderSize];
      noChunkData[0] = (byte)'X';
      noChunkData[1] = (byte)'c';
      noChunkData[2] = (byte)'u';
      noChunkData[3] = (byte)'r';
      BinaryPrimitives.WriteUInt32LittleEndian(noChunkData.AsSpan(4), (uint)fileHeaderSize);
      BinaryPrimitives.WriteUInt32LittleEndian(noChunkData.AsSpan(8), 0x00010000);
      BinaryPrimitives.WriteUInt32LittleEndian(noChunkData.AsSpan(12), 0);
      return noChunkData;
    }

    var imageChunkStart = fileHeaderSize + tocEntrySize;
    var totalSize = imageChunkStart + imageChunkHeaderSize + pixelData.Length;
    var data = new byte[totalSize];
    var span = data.AsSpan();

    data[0] = (byte)'X';
    data[1] = (byte)'c';
    data[2] = (byte)'u';
    data[3] = (byte)'r';
    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), (uint)fileHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), 0x00010000);
    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), 1);

    var tocSpan = span.Slice(fileHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(tocSpan, 0xFFFD0002);
    BinaryPrimitives.WriteUInt32LittleEndian(tocSpan.Slice(4), (uint)nominalSize);
    BinaryPrimitives.WriteUInt32LittleEndian(tocSpan.Slice(8), (uint)imageChunkStart);

    var chunkSpan = span.Slice(imageChunkStart);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan, (uint)imageChunkHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(4), 0xFFFD0002);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(8), (uint)nominalSize);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(12), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(16), (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(20), (uint)height);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(24), (uint)xhot);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(28), (uint)yhot);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkSpan.Slice(32), (uint)delay);

    Array.Copy(pixelData, 0, data, imageChunkStart + imageChunkHeaderSize, pixelData.Length);

    return data;
  }
}
