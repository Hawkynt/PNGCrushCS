using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IconLibrary;

namespace FileFormat.IconLibrary.Tests;

[TestFixture]
public sealed class IconLibraryReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IconLibraryReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".icl"));
    Assert.Throws<FileNotFoundException>(() => IconLibraryReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IconLibraryReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IconLibraryReader.FromBytes(new byte[3]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_Parses() {
    var data = new byte[22];
    data[0] = 0; data[1] = 0; // reserved
    data[2] = 1; data[3] = 0; // type = 1 (icon)
    data[4] = 1; data[5] = 0; // count = 1
    data[6] = 16; // width
    data[7] = 16; // height

    var result = IconLibraryReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
    Assert.That(result.RawData.Length, Is.EqualTo(22));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DefaultDimensions_WhenNoIcoHeader() {
    var data = new byte[10];
    data[0] = 0xFF; // not a valid ICO header

    var result = IconLibraryReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(32));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenRead_PreservesData() {
    var rawData = new byte[22];
    rawData[0] = 0; rawData[1] = 0;
    rawData[2] = 1; rawData[3] = 0;
    rawData[4] = 1; rawData[5] = 0;
    rawData[6] = 32; rawData[7] = 32;

    var original = new IconLibraryFile { RawData = rawData, Width = 32, Height = 32 };

    var bytes = IconLibraryWriter.ToBytes(original);
    var restored = IconLibraryReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    Assert.That(restored.Width, Is.EqualTo(32));
    Assert.That(restored.Height, Is.EqualTo(32));
  }
}

