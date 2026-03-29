using System;
using FileFormat.CoCo3;

namespace FileFormat.CoCo3.Tests;

[TestFixture]
public sealed class CoCo3WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces32000Bytes() {
    var file = new CoCo3File { RawData = new byte[32000] };

    var bytes = CoCo3Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCo3Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[32000];
    rawData[0] = 0x3F;
    rawData[1] = 0x2A;
    rawData[159] = 0x7F;
    rawData[31999] = 0xDE;

    var file = new CoCo3File { RawData = rawData };

    var bytes = CoCo3Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[1], Is.EqualTo(0x2A));
    Assert.That(bytes[159], Is.EqualTo(0x7F));
    Assert.That(bytes[31999], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new CoCo3File { RawData = rawData };

    var bytes = CoCo3Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(32000));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[31999], Is.EqualTo(0x00));
  }
}
