using System;
using FileFormat.AppleIIHgr;

namespace FileFormat.AppleIIHgr.Tests;

[TestFixture]
public sealed class AppleIIHgrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces8192Bytes() {
    var file = new AppleIIHgrFile {
      RawData = new byte[8192]
    };

    var bytes = AppleIIHgrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8192));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIHgrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[8192];
    rawData[0] = 0x7F;
    rawData[1] = 0x2A;
    rawData[127] = 0x55;
    rawData[8191] = 0xDE;

    var file = new AppleIIHgrFile {
      RawData = rawData
    };

    var bytes = AppleIIHgrWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x7F));
    Assert.That(bytes[1], Is.EqualTo(0x2A));
    Assert.That(bytes[127], Is.EqualTo(0x55));
    Assert.That(bytes[8191], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new AppleIIHgrFile {
      RawData = rawData
    };

    var bytes = AppleIIHgrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8192));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[8191], Is.EqualTo(0x00));
  }
}
