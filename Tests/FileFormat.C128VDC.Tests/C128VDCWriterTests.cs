using System;
using FileFormat.C128VDC;

namespace FileFormat.C128VDC.Tests;

[TestFixture]
public sealed class C128VDCWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128VDCWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesExpectedFileSize() {
    var file = new C128VDCFile {
      RawData = new byte[C128VDCFile.ExpectedFileSize]
    };

    var bytes = C128VDCWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(C128VDCFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    rawData[0] = 0xAB;
    rawData[79] = 0xCD;
    rawData[8000] = 0xEF;
    rawData[15999] = 0xDE;

    var file = new C128VDCFile { RawData = rawData };

    var bytes = C128VDCWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAB));
    Assert.That(bytes[79], Is.EqualTo(0xCD));
    Assert.That(bytes[8000], Is.EqualTo(0xEF));
    Assert.That(bytes[15999], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new C128VDCFile { RawData = rawData };

    var bytes = C128VDCWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(C128VDCFile.ExpectedFileSize));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[15999], Is.EqualTo(0x00));
  }
}
