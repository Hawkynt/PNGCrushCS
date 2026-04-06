using System;
using System.IO;
using FileFormat.PrintfoxPagefox;
using FileFormat.Core;

namespace FileFormat.PrintfoxPagefox.Tests;

[TestFixture]
public sealed class PrintfoxPagefoxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintfoxPagefoxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bs"));
    Assert.Throws<FileNotFoundException>(() => PrintfoxPagefoxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintfoxPagefoxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PrintfoxPagefoxReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[8000];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = PrintfoxPagefoxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.RawData.Length, Is.EqualTo(8000));
  }
}

[TestFixture]
public sealed class PrintfoxPagefoxRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[8000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 19 % 256);

    var original = new PrintfoxPagefoxFile { RawData = rawData };

    var bytes = PrintfoxPagefoxWriter.ToBytes(original);
    var restored = PrintfoxPagefoxReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

