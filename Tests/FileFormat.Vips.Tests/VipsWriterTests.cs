using System;
using System.Buffers.Binary;
using FileFormat.Vips;

namespace FileFormat.Vips.Tests;

[TestFixture]
public sealed class VipsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => VipsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      PixelData = new byte[3]
    };

    var bytes = VipsWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(VipsReader.MagicValue));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions() {
    var file = new VipsFile {
      Width = 320,
      Height = 240,
      Bands = 3,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = VipsWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BandsField() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      PixelData = new byte[3]
    };

    var bytes = VipsWriter.ToBytes(file);

    var bands = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
    Assert.That(bands, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BandFmtField() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = new byte[1]
    };

    var bytes = VipsWriter.ToBytes(file);

    var bandFmt = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20));
    Assert.That(bandFmt, Is.EqualTo((int)VipsBandFormat.UChar));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize() {
    var w = 4;
    var h = 3;
    var bands = 3;
    var file = new VipsFile {
      Width = w,
      Height = h,
      Bands = bands,
      PixelData = new byte[w * h * bands]
    };

    var bytes = VipsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(64 + w * h * bands));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_GrayscaleType() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = new byte[1]
    };

    var bytes = VipsWriter.ToBytes(file);

    var type = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28));
    Assert.That(type, Is.EqualTo(1)); // B_W
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RgbType() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      PixelData = new byte[3]
    };

    var bytes = VipsWriter.ToBytes(file);

    var type = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28));
    Assert.That(type, Is.EqualTo(22)); // RGB
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BBitsField() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      PixelData = new byte[3]
    };

    var bytes = VipsWriter.ToBytes(file);

    var bbits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(56));
    Assert.That(bbits, Is.EqualTo(8));
  }
}
