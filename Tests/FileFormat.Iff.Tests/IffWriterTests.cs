using System;
using System.Buffers.Binary;
using FileFormat.Iff;

namespace FileFormat.Iff.Tests;

[TestFixture]
public sealed class IffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => IffWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyFile_StartsWithFormSignature() {
    var bytes = IffWriter.ToBytes(new IffFile { FormType = "TEST", Chunks = [] });
    Assert.That(bytes[0], Is.EqualTo((byte)'F'));
    Assert.That(bytes[1], Is.EqualTo((byte)'O'));
    Assert.That(bytes[2], Is.EqualTo((byte)'R'));
    Assert.That(bytes[3], Is.EqualTo((byte)'M'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyFile_ContainsFormType() {
    var bytes = IffWriter.ToBytes(new IffFile { FormType = "ILBM", Chunks = [] });
    Assert.That(bytes[8], Is.EqualTo((byte)'I'));
    Assert.That(bytes[9], Is.EqualTo((byte)'L'));
    Assert.That(bytes[10], Is.EqualTo((byte)'B'));
    Assert.That(bytes[11], Is.EqualTo((byte)'M'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyFile_SizeFieldIsBigEndian() {
    var bytes = IffWriter.ToBytes(new IffFile { FormType = "TEST", Chunks = [] });
    var size = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(size, Is.EqualTo(bytes.Length - 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithChunk_IncludesChunkData() {
    var file = new IffFile {
      FormType = "TEST",
      Chunks = [new IffChunk { ChunkId = "BODY", Data = [1, 2, 3, 4] }]
    };

    var bytes = IffWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.GreaterThan(12));
  }
}
