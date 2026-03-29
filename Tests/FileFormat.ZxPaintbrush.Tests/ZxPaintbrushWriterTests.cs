using System;
using FileFormat.ZxPaintbrush;

namespace FileFormat.ZxPaintbrush.Tests;

[TestFixture]
public sealed class ZxPaintbrushWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxPaintbrushWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MinimalFile_OutputIsExactly6912Bytes() {
    var file = new ZxPaintbrushFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6912));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithExtraData_OutputIncludesExtraData() {
    var extra = new byte[50];
    extra[0] = 0xAB;
    extra[49] = 0xCD;

    var file = new ZxPaintbrushFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtraData = extra,
    };

    var bytes = ZxPaintbrushWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6912 + 50));
    Assert.That(bytes[6912], Is.EqualTo(0xAB));
    Assert.That(bytes[6961], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AttributeDataWrittenAtOffset6144() {
    var attributes = new byte[768];
    attributes[0] = 0x47;
    attributes[767] = 0x38;

    var file = new ZxPaintbrushFile {
      BitmapData = new byte[6144],
      AttributeData = attributes
    };

    var bytes = ZxPaintbrushWriter.ToBytes(file);

    Assert.That(bytes[6144], Is.EqualTo(0x47));
    Assert.That(bytes[6911], Is.EqualTo(0x38));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row0() {
    var bitmap = new byte[6144];
    bitmap[0] = 0xFF;

    var file = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row1() {
    var bitmap = new byte[6144];
    var row1Offset = 1 * 32;
    bitmap[row1Offset] = 0xAA;

    var file = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(file);

    Assert.That(bytes[256], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row64() {
    var bitmap = new byte[6144];
    var row64Offset = 64 * 32;
    bitmap[row64Offset] = 0xCC;

    var file = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(file);

    Assert.That(bytes[2048], Is.EqualTo(0xCC));
  }
}
