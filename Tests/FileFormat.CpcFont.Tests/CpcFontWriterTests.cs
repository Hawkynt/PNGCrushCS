using System;
using FileFormat.CpcFont;

namespace FileFormat.CpcFont.Tests;

[TestFixture]
public sealed class CpcFontWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces2048Bytes() {
    var file = new CpcFontFile {
      RawData = new byte[2048]
    };

    var bytes = CpcFontWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcFontWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[2048];
    rawData[0] = 0x3F;
    rawData[1] = 0x2A;
    rawData[127] = 0x7F;
    rawData[2047] = 0xDE;

    var file = new CpcFontFile {
      RawData = rawData
    };

    var bytes = CpcFontWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[1], Is.EqualTo(0x2A));
    Assert.That(bytes[127], Is.EqualTo(0x7F));
    Assert.That(bytes[2047], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new CpcFontFile {
      RawData = rawData
    };

    var bytes = CpcFontWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2048));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[2047], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputSizeMatchesExpectedFileSize() {
    var file = new CpcFontFile {
      RawData = new byte[2048]
    };

    var bytes = CpcFontWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcFontFile.ExpectedFileSize));
  }
}
