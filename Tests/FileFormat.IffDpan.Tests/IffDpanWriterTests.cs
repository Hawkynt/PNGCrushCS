using System;
using FileFormat.IffDpan;

namespace FileFormat.IffDpan.Tests;

[TestFixture]
public sealed class IffDpanWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDpanWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyRawData_ReturnsEmptyArray() {
    var file = new IffDpanFile {
      Width = 320,
      Height = 200,
      RawData = [],
    };

    var bytes = IffDpanWriter.ToBytes(file);

    Assert.That(bytes, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesRawDataLength() {
    var rawData = new byte[100];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var file = new IffDpanFile {
      Width = 320,
      Height = 200,
      RawData = rawData,
    };

    var bytes = IffDpanWriter.ToBytes(file);

    Assert.That(bytes, Has.Length.EqualTo(rawData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesRawDataContent() {
    var rawData = new byte[64];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)((i * 7 + 3) % 256);

    var file = new IffDpanFile {
      Width = 320,
      Height = 200,
      RawData = rawData,
    };

    var bytes = IffDpanWriter.ToBytes(file);

    Assert.That(bytes, Is.EqualTo(rawData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CopiesRawData_NotSameReference() {
    var rawData = new byte[] { 0x01, 0x02, 0x03 };
    var file = new IffDpanFile {
      Width = 1,
      Height = 1,
      RawData = rawData,
    };

    var bytes = IffDpanWriter.ToBytes(file);

    Assert.That(bytes, Is.Not.SameAs(rawData));
    Assert.That(bytes, Is.EqualTo(rawData));
  }
}
