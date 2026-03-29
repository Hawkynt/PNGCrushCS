using System;
using System.Buffers.Binary;
using FileFormat.Blp;

namespace FileFormat.Blp.Tests;

[TestFixture]
public sealed class BlpWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BlpWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidBgra_StartsWithBlp2Magic() {
    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = false,
      MipData = [new byte[4 * 4 * 4]],
    };

    var bytes = BlpWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    Assert.That(magic, Is.EqualTo(0x32504C42U)); // "BLP2"
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderWidth_IsPreserved() {
    var file = new BlpFile {
      Width = 16,
      Height = 8,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = false,
      MipData = [new byte[16 * 8 * 4]],
    };

    var bytes = BlpWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16));
    Assert.That(width, Is.EqualTo(16U));
    Assert.That(height, Is.EqualTo(8U));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EncodingByte_IsPreserved() {
    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.Dxt,
      AlphaDepth = 0,
      AlphaEncoding = BlpAlphaEncoding.Dxt1,
      HasMips = false,
      MipData = [new byte[8]], // 1 DXT1 block
    };

    var bytes = BlpWriter.ToBytes(file);

    Assert.That(bytes[8], Is.EqualTo((byte)BlpEncoding.Dxt));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AlphaFields_ArePreserved() {
    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.Dxt,
      AlphaDepth = 8,
      AlphaEncoding = BlpAlphaEncoding.Dxt5,
      HasMips = false,
      MipData = [new byte[16]],
    };

    var bytes = BlpWriter.ToBytes(file);

    Assert.That(bytes[9], Is.EqualTo(8));  // AlphaDepth
    Assert.That(bytes[10], Is.EqualTo(7)); // AlphaEncoding = DXT5
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMips_ByteIsSet() {
    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = true,
      MipData = [new byte[4 * 4 * 4]],
    };

    var bytes = BlpWriter.ToBytes(file);

    Assert.That(bytes[11], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BgraEncoding_TotalFileSize() {
    var pixelData = new byte[4 * 4 * 4];
    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = false,
      MipData = [pixelData],
    };

    var bytes = BlpWriter.ToBytes(file);

    // 148 header + 64 pixel data = 212
    Assert.That(bytes.Length, Is.EqualTo(148 + 4 * 4 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteEncoding_IncludesPalette() {
    var palette = new byte[1024];
    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.Palette,
      AlphaDepth = 0,
      HasMips = false,
      Palette = palette,
      MipData = [new byte[4 * 4]],
    };

    var bytes = BlpWriter.ToBytes(file);

    // 148 header + 1024 palette + 16 pixel data = 1188
    Assert.That(bytes.Length, Is.EqualTo(148 + 1024 + 4 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MipOffset_PointsToCorrectPosition() {
    var pixelData = new byte[4 * 4 * 4];
    var file = new BlpFile {
      Width = 4,
      Height = 4,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      HasMips = false,
      MipData = [pixelData],
    };

    var bytes = BlpWriter.ToBytes(file);

    var mipOffset0 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20));
    Assert.That(mipOffset0, Is.EqualTo(148U)); // Right after header, no palette
  }
}
