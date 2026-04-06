using System;
using System.IO;
using NUnit.Framework;
using FileFormat.Atari7800;
using FileFormat.Core;

namespace FileFormat.Atari7800.Tests;

[TestFixture]
public class Atari7800ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari7800Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Atari7800Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari7800Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Atari7800Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[Atari7800File.FileSize];
    var result = Atari7800Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(Atari7800File.ImageWidth));
    Assert.That(result.Height, Is.EqualTo(Atari7800File.ImageHeight));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Atari7800Reader.FromStream(null!));

  [Test]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[Atari7800File.FileSize];
    using var ms = new MemoryStream(data);
    var result = Atari7800Reader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(Atari7800File.ImageWidth));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_DataPreserved() {
    var data = new byte[Atari7800File.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = Atari7800Reader.FromBytes(data);
    var written = Atari7800Writer.ToBytes(file);
    var file2 = Atari7800Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var data = new byte[Atari7800File.FileSize];
    var file = Atari7800Reader.FromBytes(data);
    var raw = Atari7800File.ToRawImage(file);
    var file2 = Atari7800File.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_AllZeros() {
    var data = new byte[Atari7800File.FileSize];
    var file = Atari7800Reader.FromBytes(data);
    var written = Atari7800Writer.ToBytes(file);
    var file2 = Atari7800Reader.FromBytes(written);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

