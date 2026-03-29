using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.MayaIff;

namespace FileFormat.MayaIff.Tests;

[TestFixture]
public sealed class MayaIffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => MayaIffWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFOR4Magic() {
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = new byte[4]
    };

    var bytes = MayaIffWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("FOR4"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCIMGFormType() {
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = new byte[4]
    };

    var bytes = MayaIffWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("CIMG"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsTBHDChunk() {
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = false,
      PixelData = new byte[3]
    };

    var bytes = MayaIffWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("TBHD"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TBHDChunkSize_Is32() {
    var file = new MayaIffFile {
      Width = 2,
      Height = 2,
      HasAlpha = true,
      PixelData = new byte[16]
    };

    var bytes = MayaIffWriter.ToBytes(file);

    var tbhdSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
    Assert.That(tbhdSize, Is.EqualTo(32u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsAreBigEndian() {
    var file = new MayaIffFile {
      Width = 320,
      Height = 240,
      HasAlpha = true,
      PixelData = new byte[320 * 240 * 4]
    };

    var bytes = MayaIffWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
    var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24));

    Assert.That(width, Is.EqualTo(320u));
    Assert.That(height, Is.EqualTo(240u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RgbaChunkTag() {
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = new byte[4]
    };

    var bytes = MayaIffWriter.ToBytes(file);

    // After FOR4(4)+size(4)+CIMG(4)+TBHD_tag(4)+TBHD_size(4)+TBHD_data(32) = offset 52
    Assert.That(Encoding.ASCII.GetString(bytes, 52, 4), Is.EqualTo("RGBA"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RgbChunkTag() {
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = false,
      PixelData = new byte[3]
    };

    var bytes = MayaIffWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 52, 4), Is.EqualTo("RGB "));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = pixels
    };

    var bytes = MayaIffWriter.ToBytes(file);

    // Pixel data starts at offset 60 (52 + tag(4) + size(4))
    Assert.That(bytes[60], Is.EqualTo(0xAA));
    Assert.That(bytes[61], Is.EqualTo(0xBB));
    Assert.That(bytes[62], Is.EqualTo(0xCC));
    Assert.That(bytes[63], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelChunkSize_MatchesPixelData() {
    var pixelData = new byte[2 * 2 * 4];
    var file = new MayaIffFile {
      Width = 2,
      Height = 2,
      HasAlpha = true,
      PixelData = pixelData
    };

    var bytes = MayaIffWriter.ToBytes(file);

    var pixelChunkSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(56));
    Assert.That(pixelChunkSize, Is.EqualTo((uint)pixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BodySize_IsCorrect() {
    var pixelData = new byte[2 * 1 * 3]; // 6 bytes, padded to 8
    var file = new MayaIffFile {
      Width = 2,
      Height = 1,
      HasAlpha = false,
      PixelData = pixelData
    };

    var bytes = MayaIffWriter.ToBytes(file);

    var bodySize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
    var expectedBody = 4u /*CIMG*/ + (8u + 32u) /*TBHD*/ + (8u + 8u) /*RGB  chunk: 6 bytes padded to 8*/;
    Assert.That(bodySize, Is.EqualTo(expectedBody));
  }
}
