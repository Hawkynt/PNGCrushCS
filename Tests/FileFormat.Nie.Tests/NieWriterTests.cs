using System;
using System.Buffers.Binary;
using FileFormat.Nie;

namespace FileFormat.Nie.Tests;

[TestFixture]
public sealed class NieWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NieWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesMagic() {
    var data = _WriteBgra8(2, 2);
    Assert.Multiple(() => {
      Assert.That(data[0], Is.EqualTo(0x6E));
      Assert.That(data[1], Is.EqualTo(0xC3));
      Assert.That(data[2], Is.EqualTo(0xAF));
      Assert.That(data[3], Is.EqualTo(0x45));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPixelConfigByte() {
    var data = _WriteBgra8(2, 2);
    Assert.That(data[4], Is.EqualTo((byte)NiePixelConfig.Bgra8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPaddingZeros() {
    var data = _WriteBgra8(2, 2);
    Assert.Multiple(() => {
      Assert.That(data[5], Is.EqualTo(0));
      Assert.That(data[6], Is.EqualTo(0));
      Assert.That(data[7], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesDimensionsLE() {
    var data = _WriteBgra8(5, 3);
    var width = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(5));
      Assert.That(height, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bgra8_TotalSizeCorrect() {
    var data = _WriteBgra8(4, 3);
    Assert.That(data.Length, Is.EqualTo(NieFile.HeaderSize + 4 * 3 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bgra16_TotalSizeCorrect() {
    var file = new NieFile {
      Width = 4, Height = 3, PixelConfig = NiePixelConfig.Bgra16,
      PixelData = new byte[4 * 3 * 8]
    };
    var data = NieWriter.ToBytes(file);
    Assert.That(data.Length, Is.EqualTo(NieFile.HeaderSize + 4 * 3 * 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[16];
    pixels[0] = 0xAA;
    pixels[15] = 0xBB;
    var file = new NieFile {
      Width = 2, Height = 2, PixelConfig = NiePixelConfig.Bgra8, PixelData = pixels
    };
    var data = NieWriter.ToBytes(file);
    Assert.Multiple(() => {
      Assert.That(data[NieFile.HeaderSize], Is.EqualTo(0xAA));
      Assert.That(data[NieFile.HeaderSize + 15], Is.EqualTo(0xBB));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Premul8_ConfigWritten() {
    var file = new NieFile {
      Width = 1, Height = 1, PixelConfig = NiePixelConfig.BgraPremul8, PixelData = new byte[4]
    };
    var data = NieWriter.ToBytes(file);
    Assert.That(data[4], Is.EqualTo((byte)NiePixelConfig.BgraPremul8));
  }

  private static byte[] _WriteBgra8(int w, int h)
    => NieWriter.ToBytes(new NieFile {
      Width = w, Height = h, PixelConfig = NiePixelConfig.Bgra8,
      PixelData = new byte[w * h * 4]
    });
}
