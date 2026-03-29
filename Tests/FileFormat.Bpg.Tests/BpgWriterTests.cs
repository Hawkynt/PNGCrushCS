using System;
using FileFormat.Bpg;

namespace FileFormat.Bpg.Tests;

[TestFixture]
public sealed class BpgWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BpgWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes_AreCorrect() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
    };

    var bytes = BpgWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x42));
    Assert.That(bytes[1], Is.EqualTo(0x50));
    Assert.That(bytes[2], Is.EqualTo(0x47));
    Assert.That(bytes[3], Is.EqualTo(0xFB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelFormatByte_EncodesCorrectly() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
    };

    var bytes = BpgWriter.ToBytes(file);
    var pixelFormatBits = (bytes[4] >> 5) & 0x07;

    Assert.That(pixelFormatBits, Is.EqualTo((int)BpgPixelFormat.YCbCr444));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitDepthByte_EncodesCorrectly() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr420,
      BitDepth = 10,
      ColorSpace = BpgColorSpace.Rgb,
    };

    var bytes = BpgWriter.ToBytes(file);
    var bitDepthMinus8 = bytes[4] & 0x0F;

    Assert.That(bitDepthMinus8, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AlphaFlag_EncodesCorrectly() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      HasAlpha = true,
    };

    var bytes = BpgWriter.ToBytes(file);
    var alphaFlag = (bytes[4] >> 4) & 0x01;

    Assert.That(alphaFlag, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorSpaceByte_EncodesCorrectly() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr420,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.YCbCrBT709,
    };

    var bytes = BpgWriter.ToBytes(file);
    var colorSpaceBits = (bytes[5] >> 4) & 0x0F;

    Assert.That(colorSpaceBits, Is.EqualTo((int)BpgColorSpace.YCbCrBT709));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AnimationFlag_EncodesCorrectly() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      IsAnimation = true,
    };

    var bytes = BpgWriter.ToBytes(file);
    var animFlag = bytes[5] & 0x01;

    Assert.That(animFlag, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LimitedRangeFlag_EncodesCorrectly() {
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.YCbCr444,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      LimitedRange = true,
    };

    var bytes = BpgWriter.ToBytes(file);
    var limitedRangeFlag = (bytes[5] >> 1) & 0x01;

    Assert.That(limitedRangeFlag, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SmallDimensions_Ue7SingleByte() {
    var file = new BpgFile {
      Width = 100,
      Height = 50,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
    };

    var bytes = BpgWriter.ToBytes(file);

    // width=100 < 128 => 1 byte, height=50 < 128 => 1 byte
    // After magic(4) + byte4(1) + byte5(1) = offset 6
    Assert.That(bytes[6], Is.EqualTo(100));
    Assert.That(bytes[7], Is.EqualTo(50));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PictureData_IsIncluded() {
    var pixelData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    var file = new BpgFile {
      Width = 2,
      Height = 2,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = pixelData,
    };

    var bytes = BpgWriter.ToBytes(file);

    // Verify pixel data appears at end
    Assert.That(bytes[^4], Is.EqualTo(0x01));
    Assert.That(bytes[^3], Is.EqualTo(0x02));
    Assert.That(bytes[^2], Is.EqualTo(0x03));
    Assert.That(bytes[^1], Is.EqualTo(0x04));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPixelData_ProducesValidFile() {
    var file = new BpgFile {
      Width = 1,
      Height = 1,
      PixelFormat = BpgPixelFormat.Grayscale,
      BitDepth = 8,
      ColorSpace = BpgColorSpace.Rgb,
      PixelData = [],
    };

    var bytes = BpgWriter.ToBytes(file);

    // magic(4) + byte4(1) + byte5(1) + width ue7(1) + height ue7(1) + dataLen ue7(1 for 0)
    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(9));
  }
}
