using System;
using System.IO;
using NUnit.Framework;
using FileFormat.Enterprise128;
using FileFormat.Core;

namespace FileFormat.Enterprise128.Tests;

[TestFixture]
public class Enterprise128ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Enterprise128Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Enterprise128Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[Enterprise128File.FileSize];
    var result = Enterprise128Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(Enterprise128File.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(Enterprise128File.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Enterprise128Reader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[Enterprise128File.FileSize];
    using var ms = new MemoryStream(data);
    var result = Enterprise128Reader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(Enterprise128File.ImageWidth));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[Enterprise128File.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 1);
    var file = Enterprise128Reader.FromBytes(data);
    var written = Enterprise128Writer.ToBytes(file);
    var file2 = Enterprise128Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[Enterprise128File.FileSize];
    var file = Enterprise128Reader.FromBytes(data);
    var raw = Enterprise128File.ToRawImage(file);
    var file2 = Enterprise128File.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[Enterprise128File.FileSize];
    var file = Enterprise128Reader.FromBytes(data);
    var written = Enterprise128Writer.ToBytes(file);
    var file2 = Enterprise128Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

