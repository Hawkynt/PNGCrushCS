using System;
using System.Buffers.Binary;
using FileFormat.Pcd;

namespace FileFormat.Pcd.Tests;

[TestFixture]
public sealed class PcdWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreambleIsAllZeros() {
    var file = new PcdFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x80]
    };

    var bytes = PcdWriter.ToBytes(file);

    for (var i = 0; i < PcdFile.PreambleSize; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Preamble byte at offset {i} should be zero.");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicAtOffset2048() {
    var file = new PcdFile {
      Width = 1,
      Height = 1,
      PixelData = [0, 0, 0]
    };

    var bytes = PcdWriter.ToBytes(file);

    var magic = new byte[PcdFile.Magic.Length];
    Array.Copy(bytes, PcdFile.PreambleSize, magic, 0, PcdFile.Magic.Length);
    Assert.That(magic, Is.EqualTo(PcdFile.Magic));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsAtCorrectOffset() {
    var file = new PcdFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = PcdWriter.ToBytes(file);

    var dimOffset = PcdFile.PreambleSize + PcdFile.Magic.Length;
    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(dimOffset));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(dimOffset + 2));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtCorrectOffset() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new PcdFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = PcdWriter.ToBytes(file);

    Assert.That(bytes[PcdFile.HeaderSize], Is.EqualTo(0xAA));
    Assert.That(bytes[PcdFile.HeaderSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[PcdFile.HeaderSize + 2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSize() {
    var w = 4;
    var h = 3;
    var file = new PcdFile {
      Width = w,
      Height = h,
      PixelData = new byte[w * h * 3]
    };

    var bytes = PcdWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(PcdFile.HeaderSize + w * h * 3));
  }
}
