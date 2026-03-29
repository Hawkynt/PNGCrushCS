using System;
using FileFormat.Trs80;

namespace FileFormat.Trs80.Tests;

[TestFixture]
public sealed class Trs80WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces6144Bytes() {
    var file = new Trs80File {
      RawData = new byte[6144]
    };

    var bytes = Trs80Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6144));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Trs80Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[6144];
    rawData[0] = 0x3F;
    rawData[1] = 0x2A;
    rawData[127] = 0x7F;
    rawData[6143] = 0xDE;

    var file = new Trs80File {
      RawData = rawData
    };

    var bytes = Trs80Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[1], Is.EqualTo(0x2A));
    Assert.That(bytes[127], Is.EqualTo(0x7F));
    Assert.That(bytes[6143], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new Trs80File {
      RawData = rawData
    };

    var bytes = Trs80Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6144));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[6143], Is.EqualTo(0x00));
  }
}
