using System;
using FileFormat.Pagefox;

namespace FileFormat.Pagefox.Tests;

[TestFixture]
public sealed class PagefoxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PagefoxWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputSize_Is16384() {
    var file = new PagefoxFile { RawData = new byte[16384] };

    var bytes = PagefoxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[16384];
    rawData[0] = 0x3F;
    rawData[79] = 0x7F;
    rawData[16383] = 0xDE;

    var file = new PagefoxFile { RawData = rawData };
    var bytes = PagefoxWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[79], Is.EqualTo(0x7F));
    Assert.That(bytes[16383], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new PagefoxFile { RawData = rawData };
    var bytes = PagefoxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[16383], Is.EqualTo(0x00));
  }
}
