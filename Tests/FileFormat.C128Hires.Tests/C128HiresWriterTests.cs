using System;
using FileFormat.C128Hires;

namespace FileFormat.C128Hires.Tests;

[TestFixture]
public sealed class C128HiresWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128HiresWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesExpectedFileSize() {
    var file = new C128HiresFile {
      RawData = new byte[C128HiresFile.ExpectedFileSize]
    };

    var bytes = C128HiresWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(C128HiresFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var rawData = new byte[C128HiresFile.ExpectedFileSize];
    rawData[0] = 0xAB;
    rawData[7] = 0xCD;
    rawData[4000] = 0xEF;
    rawData[7999] = 0xDE;

    var file = new C128HiresFile { RawData = rawData };

    var bytes = C128HiresWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAB));
    Assert.That(bytes[7], Is.EqualTo(0xCD));
    Assert.That(bytes[4000], Is.EqualTo(0xEF));
    Assert.That(bytes[7999], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var rawData = new byte[10];
    rawData[0] = 0xFF;

    var file = new C128HiresFile { RawData = rawData };

    var bytes = C128HiresWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(C128HiresFile.ExpectedFileSize));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[7999], Is.EqualTo(0x00));
  }
}
