using System;
using System.IO;
using FileFormat.EpaBios;
using FileFormat.Core;

namespace FileFormat.EpaBios.Tests;

[TestFixture]
public class EpaBiosReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EpaBiosReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => EpaBiosReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EpaBiosReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => EpaBiosReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[714];
    var result = EpaBiosReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(136));
    Assert.That(result.Height, Is.EqualTo(84));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EpaBiosReader.FromStream(null!));
}

[TestFixture]
public class EpaBiosWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EpaBiosWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is714() {
    var file = new EpaBiosFile { PixelData = new byte[714] };
    var bytes = EpaBiosWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(714));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[714];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new EpaBiosFile { PixelData = data };
    var bytes = EpaBiosWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[714];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = EpaBiosReader.FromBytes(original);
    var written = EpaBiosWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[714];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = EpaBiosReader.FromFile(new FileInfo(tmp));
      var written = EpaBiosWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is136()
    => Assert.That(EpaBiosFile.FixedWidth, Is.EqualTo(136));

  [Test]
  public void FixedHeight_Is84()
    => Assert.That(EpaBiosFile.FixedHeight, Is.EqualTo(84));

  [Test]
  public void FileSize_Is714()
    => Assert.That(EpaBiosFile.FileSize, Is.EqualTo(714));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EpaBiosFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EpaBiosFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 136, Height = 84, Format = PixelFormat.Rgb24, PixelData = new byte[136 * 84 * 3] };
    Assert.Throws<ArgumentException>(() => EpaBiosFile.FromRawImage(raw));
  }
}
