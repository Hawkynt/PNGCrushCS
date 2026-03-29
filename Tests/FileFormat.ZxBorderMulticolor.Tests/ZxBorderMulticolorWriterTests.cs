using System;
using FileFormat.ZxBorderMulticolor;

namespace FileFormat.ZxBorderMulticolor.Tests;

[TestFixture]
public sealed class ZxBorderMulticolorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxBorderMulticolorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputSizeMatches() {
    var file = new ZxBorderMulticolorFile {
      BitmapData = new byte[ZxBorderMulticolorReader.BitmapSize],
      AttributeData = new byte[ZxBorderMulticolorReader.AttributeSize],
      BorderData = new byte[ZxBorderMulticolorReader.BorderSize],
    };

    var bytes = ZxBorderMulticolorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(ZxBorderMulticolorReader.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AttributeDataWrittenAfterBitmap() {
    var attributes = new byte[ZxBorderMulticolorReader.AttributeSize];
    attributes[0] = 0x47;
    attributes[1] = 0x23;

    var file = new ZxBorderMulticolorFile {
      BitmapData = new byte[ZxBorderMulticolorReader.BitmapSize],
      AttributeData = attributes,
      BorderData = new byte[ZxBorderMulticolorReader.BorderSize],
    };

    var bytes = ZxBorderMulticolorWriter.ToBytes(file);

    Assert.That(bytes[ZxBorderMulticolorReader.BitmapSize], Is.EqualTo(0x47));
    Assert.That(bytes[ZxBorderMulticolorReader.BitmapSize + 1], Is.EqualTo(0x23));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BorderDataWrittenAfterAttributes() {
    var border = new byte[ZxBorderMulticolorReader.BorderSize];
    border[0] = 0xAA;
    border[1] = 0xBB;

    var file = new ZxBorderMulticolorFile {
      BitmapData = new byte[ZxBorderMulticolorReader.BitmapSize],
      AttributeData = new byte[ZxBorderMulticolorReader.AttributeSize],
      BorderData = border,
    };

    var bytes = ZxBorderMulticolorWriter.ToBytes(file);

    var borderOffset = ZxBorderMulticolorReader.BitmapSize + ZxBorderMulticolorReader.AttributeSize;
    Assert.That(bytes[borderOffset], Is.EqualTo(0xAA));
    Assert.That(bytes[borderOffset + 1], Is.EqualTo(0xBB));
  }
}
