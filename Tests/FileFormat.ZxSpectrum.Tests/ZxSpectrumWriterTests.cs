using System;
using FileFormat.ZxSpectrum;

namespace FileFormat.ZxSpectrum.Tests;

[TestFixture]
public sealed class ZxSpectrumWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly6912Bytes() {
    var file = new ZxSpectrumFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6912));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxSpectrumWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AttributeDataWrittenAtOffset6144() {
    var attributes = new byte[768];
    attributes[0] = 0x47; // bright=1, paper=0, ink=7
    attributes[767] = 0x38; // paper=7, ink=0

    var file = new ZxSpectrumFile {
      BitmapData = new byte[6144],
      AttributeData = attributes
    };

    var bytes = ZxSpectrumWriter.ToBytes(file);

    Assert.That(bytes[6144], Is.EqualTo(0x47));
    Assert.That(bytes[6911], Is.EqualTo(0x38));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row0() {
    // Row 0: third=0, characterRow=0, pixelLine=0
    // Address = 0*2048 + 0*256 + 0*32 = 0
    var bitmap = new byte[6144];
    bitmap[0] = 0xFF; // first byte of row 0

    var file = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row1() {
    // Row 1: third=0, characterRow=0, pixelLine=1
    // Address = 0*2048 + 1*256 + 0*32 = 256
    var bitmap = new byte[6144];
    var row1Offset = 1 * 32; // linear offset for row 1
    bitmap[row1Offset] = 0xAA;

    var file = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(file);

    Assert.That(bytes[256], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row8() {
    // Row 8: third=0, characterRow=1, pixelLine=0
    // Address = 0*2048 + 0*256 + 1*32 = 32
    var bitmap = new byte[6144];
    var row8Offset = 8 * 32; // linear offset for row 8
    bitmap[row8Offset] = 0x55;

    var file = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(file);

    Assert.That(bytes[32], Is.EqualTo(0x55));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row64() {
    // Row 64: third=1, characterRow=0, pixelLine=0
    // Address = 1*2048 + 0*256 + 0*32 = 2048
    var bitmap = new byte[6144];
    var row64Offset = 64 * 32; // linear offset for row 64
    bitmap[row64Offset] = 0xCC;

    var file = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(file);

    Assert.That(bytes[2048], Is.EqualTo(0xCC));
  }
}
