using System;
using FileFormat.Msx;

namespace FileFormat.Msx.Tests;

[TestFixture]
public sealed class MsxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Sc2_SizeMatchesRawData() {
    var file = new MsxFile {
      Width = 256,
      Height = 192,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1,
      PixelData = new byte[16384],
      HasBloadHeader = false
    };

    var bytes = MsxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Sc5_SizeIncludesPalette() {
    var file = new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen5,
      BitsPerPixel = 4,
      PixelData = new byte[26848],
      Palette = new byte[32],
      HasBloadHeader = false
    };

    var bytes = MsxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(26880));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithBloadHeader_PrependsMagicByte() {
    var file = new MsxFile {
      Width = 256,
      Height = 192,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1,
      PixelData = new byte[16384],
      HasBloadHeader = true
    };

    var bytes = MsxWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFE));
    Assert.That(bytes.Length, Is.EqualTo(7 + 16384));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithBloadHeader_SizeIncludes7ByteHeader() {
    var file = new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen8,
      BitsPerPixel = 8,
      PixelData = new byte[54272],
      HasBloadHeader = true
    };

    var bytes = MsxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7 + 54272));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Sc8_PixelDataPreserved() {
    var pixelData = new byte[54272];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var file = new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen8,
      BitsPerPixel = 8,
      PixelData = pixelData,
      HasBloadHeader = false
    };

    var bytes = MsxWriter.ToBytes(file);

    for (var i = 0; i < pixelData.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(pixelData[i]));
  }
}
