using System;
using FileFormat.AtariHr;

namespace FileFormat.AtariHr.Tests;

[TestFixture]
public sealed class AtariHrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariHrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_ReturnsExactSize() {
    var file = new AtariHrFile { RawData = new byte[7680] };

    var bytes = AtariHrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataIsPreserved() {
    var rawData = new byte[7680];
    rawData[0] = 0x3F;
    rawData[1] = 0x2A;
    rawData[127] = 0x7F;
    rawData[7679] = 0xDE;

    var file = new AtariHrFile { RawData = rawData };

    var bytes = AtariHrWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[1], Is.EqualTo(0x2A));
    Assert.That(bytes[127], Is.EqualTo(0x7F));
    Assert.That(bytes[7679], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new AtariHrFile { RawData = rawData };

    var bytes = AtariHrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7680));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[7679], Is.EqualTo(0x00));
  }
}
