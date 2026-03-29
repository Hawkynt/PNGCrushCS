using System;
using FileFormat.AtariGr8;

namespace FileFormat.AtariGr8.Tests;

[TestFixture]
public sealed class AtariGr8WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces7680Bytes() {
    var file = new AtariGr8File {
      RawData = new byte[7680]
    };

    var bytes = AtariGr8Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr8Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[7680];
    rawData[0] = 0xAB;
    rawData[1] = 0xCD;
    rawData[39] = 0xEF;
    rawData[7679] = 0x42;

    var file = new AtariGr8File { RawData = rawData };

    var bytes = AtariGr8Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAB));
    Assert.That(bytes[1], Is.EqualTo(0xCD));
    Assert.That(bytes[39], Is.EqualTo(0xEF));
    Assert.That(bytes[7679], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new AtariGr8File { RawData = rawData };

    var bytes = AtariGr8Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7680));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[7679], Is.EqualTo(0x00));
  }
}
