using System;
using System.IO;
using NUnit.Framework;
using FileFormat.FmTowns;
using FileFormat.Core;

namespace FileFormat.FmTowns.Tests;

[TestFixture]
public class FmTownsReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => FmTownsReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FmTownsReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[FmTownsFile.FileSize];
    var result = FmTownsReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(FmTownsFile.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(FmTownsFile.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FmTownsReader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[FmTownsFile.FileSize];
    using var ms = new MemoryStream(data);
    var result = FmTownsReader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(FmTownsFile.ImageWidth));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[FmTownsFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = FmTownsReader.FromBytes(data);
    var written = FmTownsWriter.ToBytes(file);
    var file2 = FmTownsReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[FmTownsFile.FileSize];
    var file = FmTownsReader.FromBytes(data);
    var raw = FmTownsFile.ToRawImage(file);
    var file2 = FmTownsFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[FmTownsFile.FileSize];
    var file = FmTownsReader.FromBytes(data);
    var written = FmTownsWriter.ToBytes(file);
    var file2 = FmTownsReader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

