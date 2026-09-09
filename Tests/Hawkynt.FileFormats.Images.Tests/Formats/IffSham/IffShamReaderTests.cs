using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.IffSham;

namespace FileFormat.IffSham.Tests;

[TestFixture]
public sealed class IffShamReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffShamReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffShamReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sham"));
    Assert.Throws<FileNotFoundException>(() => IffShamReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffShamReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IffShamReader.FromBytes(new byte[1]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ArbitraryTwelveBytes_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IffShamReader.FromBytes(new byte[12]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WriterOutput_ParsesStructuredState() {
    var bytes = _CreateValidBytes();

    var result = IffShamReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.PixelData, Has.Length.EqualTo(320 * 200));
      Assert.That(result.ScanlinePalettes, Has.Length.EqualTo(200 * 16 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnsupportedShamVersion_ThrowsNotSupportedException() {
    var bytes = _CreateValidBytes();
    var sham = _FindChunk(bytes, "SHAM"u8);
    BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(sham), 1);

    Assert.Throws<NotSupportedException>(() => IffShamReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingShamChunk_ThrowsInvalidDataException() {
    var bytes = _CreateValidBytes();
    var sham = _FindChunk(bytes, "SHAM"u8);
    "CTBL"u8.CopyTo(bytes.AsSpan(sham - 8, 4));

    Assert.Throws<InvalidDataException>(() => IffShamReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesRawData_NotReference() {
    var bytes = _CreateValidBytes();
    var result = IffShamReader.FromBytes(bytes);

    bytes[0] = 0;

    Assert.That(result.RawData[0], Is.EqualTo((byte)'F'));
  }

  private static byte[] _CreateValidBytes() {
    return IffShamWriter.ToBytes(new() {
      Width = 320,
      Height = 200,
      RawData = [],
      PixelData = new byte[320 * 200],
      ScanlinePalettes = new byte[200 * 16 * 3],
    });
  }

  private static int _FindChunk(byte[] data, ReadOnlySpan<byte> id) {
    var end = 8 + BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4, 4));
    for (var offset = 12; offset + 8 <= end;) {
      var size = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 4, 4));
      if (data.AsSpan(offset, 4).SequenceEqual(id))
        return offset + 8;
      offset += 8 + size + (size & 1);
    }

    Assert.Fail($"Chunk {System.Text.Encoding.ASCII.GetString(id)} was not found.");
    return -1;
  }
}
