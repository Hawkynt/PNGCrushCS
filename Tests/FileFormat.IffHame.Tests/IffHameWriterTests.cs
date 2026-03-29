using System;
using FileFormat.IffHame;

namespace FileFormat.IffHame.Tests;

[TestFixture]
public sealed class IffHameWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffHameWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesRawDataLength() {
    var rawData = new byte[50];
    rawData[0] = 0xAB;
    rawData[49] = 0xCD;

    var file = new IffHameFile { RawData = rawData };
    var bytes = IffHameWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(50));
    Assert.That(bytes[0], Is.EqualTo(0xAB));
    Assert.That(bytes[49], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyRawData_ReturnsEmptyArray() {
    var file = new IffHameFile { RawData = [] };
    var bytes = IffHameWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(0));
  }
}
