using System;
using FileFormat.IffSham;

namespace FileFormat.IffSham.Tests;

[TestFixture]
public sealed class IffShamWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffShamWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesRawDataLength() {
    var rawData = new byte[50];
    rawData[0] = 0xAB;
    rawData[49] = 0xCD;

    var file = new IffShamFile { RawData = rawData };
    var bytes = IffShamWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(50));
    Assert.That(bytes[0], Is.EqualTo(0xAB));
    Assert.That(bytes[49], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyRawData_ReturnsEmptyArray() {
    var file = new IffShamFile { RawData = [] };
    var bytes = IffShamWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(0));
  }
}
