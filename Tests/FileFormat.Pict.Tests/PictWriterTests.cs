using System;
using System.Buffers.Binary;
using FileFormat.Pict;

namespace FileFormat.Pict.Tests;

[TestFixture]
public sealed class PictWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Has512BytePreamble() {
    var file = new PictFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PictWriter.ToBytes(file);

    // First 512 bytes should be all zeros
    for (var i = 0; i < 512; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Preamble byte at offset {i} should be 0");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasVersionOpcode() {
    var file = new PictFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PictWriter.ToBytes(file);

    // After preamble (512) + picture size (2) + bounding rect (8) = offset 522
    var versionOpcode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(522));
    Assert.That(versionOpcode, Is.EqualTo((ushort)PictOpcode.Version));

    var versionArg = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(524));
    Assert.That(versionArg, Is.EqualTo(0x02FF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasDirectBitsOpcode() {
    var file = new PictFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PictWriter.ToBytes(file);

    // After preamble (512) + picture size (2) + bounding rect (8) + version (4) + headerOp (2+24) = offset 552
    var directBitsOpcode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(552));
    Assert.That(directBitsOpcode, Is.EqualTo((ushort)PictOpcode.DirectBitsRect));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasEndOpcode() {
    var file = new PictFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PictWriter.ToBytes(file);

    // Last 2 bytes should be EndOfPicture
    var endOpcode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bytes.Length - 2));
    Assert.That(endOpcode, Is.EqualTo((ushort)PictOpcode.EndOfPicture));
  }
}
