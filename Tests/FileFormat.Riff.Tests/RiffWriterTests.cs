using System;
using System.Buffers.Binary;
using FileFormat.Riff;

namespace FileFormat.Riff.Tests;

[TestFixture]
public sealed class RiffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() =>
    Assert.Throws<ArgumentNullException>(() => RiffWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyFile_StartsWithRiffSignature() {
    var bytes = RiffWriter.ToBytes(new RiffFile { FormType = "TEST" });
    Assert.That(bytes[0], Is.EqualTo((byte)'R'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
    Assert.That(bytes[2], Is.EqualTo((byte)'F'));
    Assert.That(bytes[3], Is.EqualTo((byte)'F'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyFile_ContainsFormType() {
    var bytes = RiffWriter.ToBytes(new RiffFile { FormType = "WEBP" });
    Assert.That(bytes[8], Is.EqualTo((byte)'W'));
    Assert.That(bytes[9], Is.EqualTo((byte)'E'));
    Assert.That(bytes[10], Is.EqualTo((byte)'B'));
    Assert.That(bytes[11], Is.EqualTo((byte)'P'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyFile_SizeFieldMatchesLength() {
    var bytes = RiffWriter.ToBytes(new RiffFile { FormType = "TEST" });
    var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(size, Is.EqualTo(bytes.Length - 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithChunk_IncludesChunkData() {
    var file = new RiffFile {
      FormType = "TEST",
      Chunks = [new RiffChunk { Id = "data", Data = [1, 2, 3, 4] }]
    };

    var bytes = RiffWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.GreaterThan(12));
  }
}
