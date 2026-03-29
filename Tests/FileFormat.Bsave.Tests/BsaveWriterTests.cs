using System;
using System.Buffers.Binary;
using FileFormat.Bsave;

namespace FileFormat.Bsave.Tests;

[TestFixture]
public sealed class BsaveWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagicByte() {
    var file = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Vga320x200x256,
      PixelData = new byte[64000]
    };

    var bytes = BsaveWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(BsaveHeader.MagicValue));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VgaSegment_IsA000() {
    var file = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Vga320x200x256,
      PixelData = new byte[64000]
    };

    var bytes = BsaveWriter.ToBytes(file);
    var segment = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1));

    Assert.That(segment, Is.EqualTo(0xA000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CgaSegment_IsB800() {
    var file = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Cga320x200x4,
      PixelData = new byte[16384]
    };

    var bytes = BsaveWriter.ToBytes(file);
    var segment = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1));

    Assert.That(segment, Is.EqualTo(0xB800));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LengthField_MatchesPixelDataLength() {
    var pixelData = new byte[64000];
    var file = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Vga320x200x256,
      PixelData = pixelData
    };

    var bytes = BsaveWriter.ToBytes(file);
    var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5));

    Assert.That(length, Is.EqualTo(64000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var pixelData = new byte[100];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var file = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Cga320x200x4,
      PixelData = pixelData
    };

    var bytes = BsaveWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BsaveHeader.StructSize + pixelData.Length));
    for (var i = 0; i < pixelData.Length; ++i)
      Assert.That(bytes[BsaveHeader.StructSize + i], Is.EqualTo(pixelData[i]));
  }
}
