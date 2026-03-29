using System;
using System.Buffers.Binary;
using FileFormat.Mrc;

namespace FileFormat.Mrc.Tests;

[TestFixture]
public sealed class MrcWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MrcWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesAtLeast1024ByteHeader() {
    var file = new MrcFile {
      Width = 2,
      Height = 2,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[4],
    };

    var bytes = MrcWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(MrcFile.HeaderSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSize_IsHeaderPlusExtPlusPixels() {
    var file = new MrcFile {
      Width = 4,
      Height = 3,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[12],
    };

    var bytes = MrcWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(MrcFile.HeaderSize + 12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MapMagicAtOffset208() {
    var file = new MrcFile {
      Width = 1,
      Height = 1,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[1],
    };

    var bytes = MrcWriter.ToBytes(file);

    Assert.That(bytes[208], Is.EqualTo((byte)'M'));
    Assert.That(bytes[209], Is.EqualTo((byte)'A'));
    Assert.That(bytes[210], Is.EqualTo((byte)'P'));
    Assert.That(bytes[211], Is.EqualTo((byte)' '));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MachineStampAtOffset212() {
    var file = new MrcFile {
      Width = 1,
      Height = 1,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[1],
    };

    var bytes = MrcWriter.ToBytes(file);

    Assert.That(bytes[212], Is.EqualTo(MrcFile.MachineStampLE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesDimensionsLE() {
    var file = new MrcFile {
      Width = 256,
      Height = 128,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[256 * 128],
    };

    var bytes = MrcWriter.ToBytes(file);
    var span = bytes.AsSpan();

    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span), Is.EqualTo(256));
    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span[4..]), Is.EqualTo(128));
    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span[8..]), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesModeLE() {
    var file = new MrcFile {
      Width = 2,
      Height = 2,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[4],
    };

    var bytes = MrcWriter.ToBytes(file);
    var span = bytes.AsSpan();

    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span[12..]), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesNsymbtLE() {
    var extHeader = new byte[] { 0x01, 0x02, 0x03 };
    var file = new MrcFile {
      Width = 1,
      Height = 1,
      Sections = 1,
      Mode = 0,
      ExtendedHeader = extHeader,
      PixelData = new byte[1],
    };

    var bytes = MrcWriter.ToBytes(file);
    var span = bytes.AsSpan();

    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span[92..]), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new MrcFile {
      Width = 2,
      Height = 2,
      Sections = 1,
      Mode = 0,
      PixelData = pixels,
    };

    var bytes = MrcWriter.ToBytes(file);

    Assert.That(bytes[MrcFile.HeaderSize], Is.EqualTo(0xAA));
    Assert.That(bytes[MrcFile.HeaderSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[MrcFile.HeaderSize + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[MrcFile.HeaderSize + 3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ExtendedHeaderPreserved() {
    var extHeader = new byte[] { 0x11, 0x22, 0x33, 0x44 };
    var file = new MrcFile {
      Width = 1,
      Height = 1,
      Sections = 1,
      Mode = 0,
      ExtendedHeader = extHeader,
      PixelData = new byte[1],
    };

    var bytes = MrcWriter.ToBytes(file);

    Assert.That(bytes[MrcFile.HeaderSize], Is.EqualTo(0x11));
    Assert.That(bytes[MrcFile.HeaderSize + 1], Is.EqualTo(0x22));
    Assert.That(bytes[MrcFile.HeaderSize + 2], Is.EqualTo(0x33));
    Assert.That(bytes[MrcFile.HeaderSize + 3], Is.EqualTo(0x44));
    // Pixel data starts after extended header
    Assert.That(bytes.Length, Is.EqualTo(MrcFile.HeaderSize + 4 + 1));
  }
}
