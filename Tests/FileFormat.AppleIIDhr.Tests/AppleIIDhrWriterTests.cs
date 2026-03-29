using System;
using FileFormat.AppleIIDhr;

namespace FileFormat.AppleIIDhr.Tests;

[TestFixture]
public sealed class AppleIIDhrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces16384Bytes() {
    var file = new AppleIIDhrFile {
      RawData = new byte[16384]
    };

    var bytes = AppleIIDhrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIDhrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[16384];
    rawData[0] = 0x7F;
    rawData[8192] = 0x55;
    rawData[16383] = 0xDE;

    var file = new AppleIIDhrFile {
      RawData = rawData
    };

    var bytes = AppleIIDhrWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x7F));
    Assert.That(bytes[8192], Is.EqualTo(0x55));
    Assert.That(bytes[16383], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new AppleIIDhrFile {
      RawData = rawData
    };

    var bytes = AppleIIDhrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[16383], Is.EqualTo(0x00));
  }
}
