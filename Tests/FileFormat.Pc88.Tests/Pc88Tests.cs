using System;
using System.IO;
using NUnit.Framework;
using FileFormat.Pc88;
using FileFormat.Core;

namespace FileFormat.Pc88.Tests;

[TestFixture]
public class Pc88ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Pc88Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Pc88Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Pc88Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Pc88Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[Pc88File.FileSize];
    var result = Pc88Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(Pc88File.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(Pc88File.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Pc88Reader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[Pc88File.FileSize];
    using var ms = new MemoryStream(data);
    var result = Pc88Reader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(Pc88File.ImageWidth));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[Pc88File.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 1);
    var file = Pc88Reader.FromBytes(data);
    var written = Pc88Writer.ToBytes(file);
    var file2 = Pc88Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[Pc88File.FileSize];
    var file = Pc88Reader.FromBytes(data);
    var raw = Pc88File.ToRawImage(file);
    var file2 = Pc88File.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[Pc88File.FileSize];
    var file = Pc88Reader.FromBytes(data);
    var written = Pc88Writer.ToBytes(file);
    var file2 = Pc88Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

