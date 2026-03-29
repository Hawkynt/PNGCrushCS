using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Pdn;

namespace FileFormat.Pdn.Tests;

[TestFixture]
public sealed class PdnWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdnWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicIsCorrect() {
    var file = new PdnFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[4],
    };

    var bytes = PdnWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("PDN3"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionIsWritten() {
    var file = new PdnFile {
      Version = 3,
      Width = 1,
      Height = 1,
      PixelData = new byte[4],
    };

    var bytes = PdnWriter.ToBytes(file);
    var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(version, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReservedIsZero() {
    var file = new PdnFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[4],
    };

    var bytes = PdnWriter.ToBytes(file);
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));

    Assert.That(reserved, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsAreCorrect() {
    var file = new PdnFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 4],
    };

    var bytes = PdnWriter.ToBytes(file);
    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputLargerThanHeader() {
    var file = new PdnFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[4],
    };

    var bytes = PdnWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(PdnReader.HEADER_SIZE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CompressedDataPresent() {
    var file = new PdnFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[16],
    };

    var bytes = PdnWriter.ToBytes(file);

    // Gzip data starts after the 16-byte header; check for gzip magic (0x1F, 0x8B)
    Assert.That(bytes[16], Is.EqualTo(0x1F));
    Assert.That(bytes[17], Is.EqualTo(0x8B));
  }
}
