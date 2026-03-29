using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Qtif;

namespace FileFormat.Qtif.Tests;

[TestFixture]
public sealed class QtifWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QtifWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FirstAtomIsIdsc() {
    var file = new QtifFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3],
    };

    var bytes = QtifWriter.ToBytes(file);
    var atomType = Encoding.ASCII.GetString(bytes, 4, 4);

    Assert.That(atomType, Is.EqualTo("idsc"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SecondAtomIsIdat() {
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = QtifWriter.ToBytes(file);
    var idscSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
    var atomType = Encoding.ASCII.GetString(bytes, idscSize + 4, 4);

    Assert.That(atomType, Is.EqualTo("idat"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdscContainsDimensions() {
    var file = new QtifFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };

    var bytes = QtifWriter.ToBytes(file);
    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8 + 32));
    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8 + 34));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdscContainsRawCodec() {
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = QtifWriter.ToBytes(file);
    var codec = Encoding.ASCII.GetString(bytes, 8 + 4, 4);

    Assert.That(codec, Is.EqualTo("raw "));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdscContainsDepth24() {
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = QtifWriter.ToBytes(file);
    var depth = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8 + 82));

    Assert.That(depth, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdatContainsPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var bytes = QtifWriter.ToBytes(file);
    var idscSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
    var idatDataStart = idscSize + 8;

    Assert.That(bytes[idatDataStart], Is.EqualTo(0xAA));
    Assert.That(bytes[idatDataStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[idatDataStart + 2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalFileSize_IsCorrect() {
    var pixelCount = 4 * 3 * 3;
    var file = new QtifFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[pixelCount],
    };

    var bytes = QtifWriter.ToBytes(file);
    var expectedSize = (8 + 86) + (8 + pixelCount);

    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdscAtomSize_Is94() {
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = QtifWriter.ToBytes(file);
    var atomSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));

    Assert.That(atomSize, Is.EqualTo(8 + 86));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IdscResolution_Is72Dpi() {
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = QtifWriter.ToBytes(file);
    var hRes = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8 + 36));
    var vRes = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8 + 40));

    Assert.That(hRes, Is.EqualTo(0x00480000));
    Assert.That(vRes, Is.EqualTo(0x00480000));
  }
}
