using System;
using System.IO;
using FileFormat.SpeccyExtended;

namespace FileFormat.SpeccyExtended.Tests;

[TestFixture]
public sealed class SpeccyExtendedReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpeccyExtendedReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpeccyExtendedReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sxg"));
    Assert.Throws<FileNotFoundException>(() => SpeccyExtendedReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpeccyExtendedReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => SpeccyExtendedReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[SpeccyExtendedReader.FileSize];
    data[0] = (byte)'X';
    data[1] = (byte)'Y';
    data[2] = (byte)'Z';
    Assert.Throws<InvalidDataException>(() => SpeccyExtendedReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesCorrectly() {
    var data = _CreateValidSxgData();
    var result = SpeccyExtendedReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Version, Is.EqualTo(1));
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
    Assert.That(result.ExtendedAttributeData.Length, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_VersionByte_Preserved() {
    var data = _CreateValidSxgData(version: 42);
    var result = SpeccyExtendedReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AttributeDataPreserved() {
    var data = _CreateValidSxgData();
    var stdAttrOffset = SpeccyExtendedReader.HeaderSize + SpeccyExtendedReader.BitmapSize;
    for (var i = 0; i < 768; ++i)
      data[stdAttrOffset + i] = (byte)(i % 256);

    var result = SpeccyExtendedReader.FromBytes(data);

    for (var i = 0; i < 768; ++i)
      Assert.That(result.AttributeData[i], Is.EqualTo((byte)(i % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExtendedAttributeDataPreserved() {
    var data = _CreateValidSxgData();
    var extAttrOffset = SpeccyExtendedReader.HeaderSize + SpeccyExtendedReader.BitmapSize + SpeccyExtendedReader.AttributeSize;
    for (var i = 0; i < 768; ++i)
      data[extAttrOffset + i] = (byte)((i * 3) % 256);

    var result = SpeccyExtendedReader.FromBytes(data);

    for (var i = 0; i < 768; ++i)
      Assert.That(result.ExtendedAttributeData[i], Is.EqualTo((byte)((i * 3) % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _CreateValidSxgData();
    using var stream = new MemoryStream(data);

    var result = SpeccyExtendedReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  private static byte[] _CreateValidSxgData(byte version = 1) {
    var data = new byte[SpeccyExtendedReader.FileSize];
    data[0] = 0x53; // 'S'
    data[1] = 0x58; // 'X'
    data[2] = 0x47; // 'G'
    data[3] = version;
    return data;
  }
}
