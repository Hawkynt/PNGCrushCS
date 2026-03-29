using System;
using FileFormat.IffDctv;

namespace FileFormat.IffDctv.Tests;

[TestFixture]
public sealed class IffDctvWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesRawDataLength() {
    var rawData = new byte[50];
    rawData[0] = 0xAB;
    rawData[49] = 0xCD;

    var file = new IffDctvFile { RawData = rawData };
    var bytes = IffDctvWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(50));
    Assert.That(bytes[0], Is.EqualTo(0xAB));
    Assert.That(bytes[49], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyRawData_ReturnsEmptyArray() {
    var file = new IffDctvFile { RawData = [] };
    var bytes = IffDctvWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(0));
  }
}
