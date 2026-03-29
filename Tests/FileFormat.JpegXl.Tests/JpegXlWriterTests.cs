using System;
using System.Buffers.Binary;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

[TestFixture]
public sealed class JpegXlWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXlWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFtypBox() {
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = new byte[3]
    };

    var bytes = JpegXlWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[4], Is.EqualTo((byte)'f'));
      Assert.That(bytes[5], Is.EqualTo((byte)'t'));
      Assert.That(bytes[6], Is.EqualTo((byte)'y'));
      Assert.That(bytes[7], Is.EqualTo((byte)'p'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FtypBrandIsJxl() {
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = new byte[3]
    };

    var bytes = JpegXlWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[8], Is.EqualTo((byte)'j'));
      Assert.That(bytes[9], Is.EqualTo((byte)'x'));
      Assert.That(bytes[10], Is.EqualTo((byte)'l'));
      Assert.That(bytes[11], Is.EqualTo((byte)' '));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsJxlcBox() {
    var file = new JpegXlFile {
      Width = 2,
      Height = 2,
      ComponentCount = 3,
      PixelData = new byte[12]
    };

    var bytes = JpegXlWriter.ToBytes(file);

    // ftyp box size: 8 + 12 = 20 bytes
    // jxlc starts at offset 20
    var ftypSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4));

    Assert.Multiple(() => {
      Assert.That(bytes[ftypSize + 4], Is.EqualTo((byte)'j'));
      Assert.That(bytes[ftypSize + 5], Is.EqualTo((byte)'x'));
      Assert.That(bytes[ftypSize + 6], Is.EqualTo((byte)'l'));
      Assert.That(bytes[ftypSize + 7], Is.EqualTo((byte)'c'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CodestreamStartsWithSignature() {
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = new byte[3]
    };

    var bytes = JpegXlWriter.ToBytes(file);
    var ftypSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4));
    var codestreamStart = ftypSize + 8; // skip jxlc box header

    Assert.Multiple(() => {
      Assert.That(bytes[codestreamStart], Is.EqualTo(0xFF));
      Assert.That(bytes[codestreamStart + 1], Is.EqualTo(0x0A));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FtypBoxSizeMatchesPayload() {
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = new byte[3]
    };

    var bytes = JpegXlWriter.ToBytes(file);
    var ftypSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4));

    // ftyp payload: brand(4) + minor_version(4) + compatible_brand(4) = 12
    Assert.That(ftypSize, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeMatchesBoxes() {
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = new byte[3]
    };

    var bytes = JpegXlWriter.ToBytes(file);
    var ftypSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4));
    var jxlcSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(ftypSize, 4));

    Assert.That(bytes.Length, Is.EqualTo(ftypSize + jxlcSize));
  }
}
