using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Msp;

namespace FileFormat.Msp.Tests;

[TestFixture]
public sealed class MspReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MspReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MspReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".msp"));
    Assert.Throws<FileNotFoundException>(() => MspReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[16];
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[32];
    BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(0), 0x1234);
    BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(2), 0x5678);
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV1_ParsesCorrectly() {
    var pixelData = new byte[] { 0b10101010, 0b01010101 };
    var data = new byte[MspHeader.StructSize + pixelData.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), MspHeader.V1Key1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), MspHeader.V1Key2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 8);  // width
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 2);  // height
    Array.Copy(pixelData, 0, data, MspHeader.StructSize, pixelData.Length);

    var result = MspReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Version, Is.EqualTo(MspVersion.V1));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV2_ParsesCorrectly() {
    // Build a V2 file: header + scan-line map + compressed data
    var width = 8;
    var height = 2;
    var scanline1 = new byte[] { 0xFF };
    var scanline2 = new byte[] { 0x00 };

    var compressed1 = MspRleCompressor.Compress(scanline1);
    var compressed2 = MspRleCompressor.Compress(scanline2);

    var scanLineMapSize = height * 2;
    var totalSize = MspHeader.StructSize + scanLineMapSize + compressed1.Length + compressed2.Length;
    var data = new byte[totalSize];

    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), MspHeader.V2Key1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), MspHeader.V2Key2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)height);

    var offset = MspHeader.StructSize;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)compressed1.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), (ushort)compressed2.Length);
    offset += scanLineMapSize;

    Array.Copy(compressed1, 0, data, offset, compressed1.Length);
    offset += compressed1.Length;
    Array.Copy(compressed2, 0, data, offset, compressed2.Length);

    var result = MspReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Version, Is.EqualTo(MspVersion.V2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidV1_ParsesCorrectly() {
    var data = new byte[MspHeader.StructSize + 1];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), MspHeader.V1Key1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), MspHeader.V1Key2);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 1);
    data[MspHeader.StructSize] = 0xAA;

    using var stream = new MemoryStream(data);
    var result = MspReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
  }
}
