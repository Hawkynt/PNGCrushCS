using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Astc;

namespace FileFormat.Astc.Tests;

[TestFixture]
public sealed class AstcReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AstcReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".astc"));
    Assert.Throws<FileNotFoundException>(() => AstcReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => AstcReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(0), 0xDEADBEEF);
    Assert.Throws<InvalidDataException>(() => AstcReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid4x4_ParsesCorrectly() {
    var blockData = new byte[16]; // One 4x4 block = 16 bytes
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i + 1);

    var data = _BuildAstcFile(4, 4, 1, 4, 4, 1, blockData);
    var result = AstcReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Depth, Is.EqualTo(1));
    Assert.That(result.BlockDimX, Is.EqualTo(4));
    Assert.That(result.BlockDimY, Is.EqualTo(4));
    Assert.That(result.BlockDimZ, Is.EqualTo(1));
    Assert.That(result.CompressedData, Is.EqualTo(blockData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8x8_ParsesCorrectly() {
    // 16x16 image with 8x8 blocks = ceil(16/8)*ceil(16/8) = 4 blocks = 64 bytes
    var blockData = new byte[64];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 3 % 256);

    var data = _BuildAstcFile(16, 16, 1, 8, 8, 1, blockData);
    var result = AstcReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
    Assert.That(result.BlockDimX, Is.EqualTo(8));
    Assert.That(result.BlockDimY, Is.EqualTo(8));
    Assert.That(result.CompressedData, Is.EqualTo(blockData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var blockData = new byte[16];
    blockData[0] = 0xAA;
    var data = _BuildAstcFile(4, 4, 1, 4, 4, 1, blockData);

    using var stream = new MemoryStream(data);
    var result = AstcReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.CompressedData[0], Is.EqualTo(0xAA));
  }

  private static byte[] _BuildAstcFile(int width, int height, int depth, byte blockX, byte blockY, byte blockZ, byte[] blockData) {
    var data = new byte[AstcHeader.StructSize + blockData.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), AstcHeader.MagicValue);
    data[4] = blockX;
    data[5] = blockY;
    data[6] = blockZ;
    data[7] = (byte)(width & 0xFF);
    data[8] = (byte)((width >> 8) & 0xFF);
    data[9] = (byte)((width >> 16) & 0xFF);
    data[10] = (byte)(height & 0xFF);
    data[11] = (byte)((height >> 8) & 0xFF);
    data[12] = (byte)((height >> 16) & 0xFF);
    data[13] = (byte)(depth & 0xFF);
    data[14] = (byte)((depth >> 8) & 0xFF);
    data[15] = (byte)((depth >> 16) & 0xFF);
    Array.Copy(blockData, 0, data, AstcHeader.StructSize, blockData.Length);
    return data;
  }
}
