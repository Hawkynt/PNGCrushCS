using System;
using FileFormat.MsxScreen8;

namespace FileFormat.MsxScreen8.Tests;

[TestFixture]
public sealed class MsxScreen8WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen8Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithoutHeader_SizeMatchesPixelData() {
    var file = new MsxScreen8File {
      PixelData = new byte[MsxScreen8File.PixelDataSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen8Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(MsxScreen8File.PixelDataSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithBsaveHeader_PrependsMagicByte() {
    var file = new MsxScreen8File {
      PixelData = new byte[MsxScreen8File.PixelDataSize],
      HasBsaveHeader = true
    };

    var bytes = MsxScreen8Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(MsxScreen8File.BsaveMagic));
    Assert.That(bytes.Length, Is.EqualTo(MsxScreen8File.FileWithHeaderSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var file = new MsxScreen8File {
      PixelData = pixelData,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen8Writer.ToBytes(file);

    for (var i = 0; i < MsxScreen8File.PixelDataSize; ++i)
      Assert.That(bytes[i], Is.EqualTo(pixelData[i]));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithBsaveHeader_HeaderContainsAddresses() {
    var file = new MsxScreen8File {
      PixelData = new byte[MsxScreen8File.PixelDataSize],
      HasBsaveHeader = true
    };

    var bytes = MsxScreen8Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFE));
    var startAddr = bytes[1] | (bytes[2] << 8);
    Assert.That(startAddr, Is.EqualTo(0x0000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithBsaveHeader_PixelDataPreserved() {
    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var file = new MsxScreen8File {
      PixelData = pixelData,
      HasBsaveHeader = true
    };

    var bytes = MsxScreen8Writer.ToBytes(file);

    for (var i = 0; i < MsxScreen8File.PixelDataSize; ++i)
      Assert.That(bytes[MsxScreen8File.BsaveHeaderSize + i], Is.EqualTo(pixelData[i]));
  }
}
