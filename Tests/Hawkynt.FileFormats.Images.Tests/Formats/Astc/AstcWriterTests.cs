using System;
using System.Buffers.Binary;
using FileFormat.Astc;

namespace FileFormat.Astc.Tests;

[TestFixture]
public sealed class AstcWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new AstcFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      BlockDimX = 4,
      BlockDimY = 4,
      BlockDimZ = 1,
      CompressedData = new byte[16]
    };

    var bytes = AstcWriter.ToBytes(file);

    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0)), Is.EqualTo(AstcHeader.MagicValue));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectBlockDims() {
    var file = new AstcFile {
      Width = 8,
      Height = 8,
      Depth = 1,
      BlockDimX = 8,
      BlockDimY = 8,
      BlockDimZ = 1,
      CompressedData = new byte[16]
    };

    var bytes = AstcWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(8));
    Assert.That(bytes[5], Is.EqualTo(8));
    Assert.That(bytes[6], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectDimensions() {
    var file = new AstcFile {
      Width = 256,
      Height = 512,
      Depth = 1,
      BlockDimX = 4,
      BlockDimY = 4,
      BlockDimZ = 1,
      CompressedData = new byte[16]
    };

    var bytes = AstcWriter.ToBytes(file);

    // Width = 256 = 0x000100 LE => bytes[7]=0x00, bytes[8]=0x01, bytes[9]=0x00
    Assert.That(bytes[7], Is.EqualTo(0x00));
    Assert.That(bytes[8], Is.EqualTo(0x01));
    Assert.That(bytes[9], Is.EqualTo(0x00));

    // Height = 512 = 0x000200 LE => bytes[10]=0x00, bytes[11]=0x02, bytes[12]=0x00
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[11], Is.EqualTo(0x02));
    Assert.That(bytes[12], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var compressedData = new byte[32];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i * 7 % 256);

    var file = new AstcFile {
      Width = 8,
      Height = 8,
      Depth = 1,
      BlockDimX = 4,
      BlockDimY = 4,
      BlockDimZ = 1,
      CompressedData = compressedData
    };

    var bytes = AstcWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(AstcHeader.StructSize + compressedData.Length));
    for (var i = 0; i < compressedData.Length; ++i)
      Assert.That(bytes[AstcHeader.StructSize + i], Is.EqualTo(compressedData[i]), $"Mismatch at compressed data index {i}");
  }
}
