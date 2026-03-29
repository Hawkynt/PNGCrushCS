using System;
using FileFormat.MsxFont;

namespace FileFormat.MsxFont.Tests;

[TestFixture]
public sealed class MsxFontWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxFontWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputSize_Is2048() {
    var file = new MsxFontFile { RawData = new byte[2048] };

    var bytes = MsxFontWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[2048];
    rawData[0] = 0x3F;
    rawData[100] = 0x7F;
    rawData[2047] = 0xDE;

    var file = new MsxFontFile { RawData = rawData };
    var bytes = MsxFontWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[100], Is.EqualTo(0x7F));
    Assert.That(bytes[2047], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new MsxFontFile { RawData = rawData };
    var bytes = MsxFontWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2048));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[2047], Is.EqualTo(0x00));
  }
}
