using System;
using System.IO;
using FileFormat.Zx81;
using FileFormat.Core;

namespace FileFormat.Zx81.Tests;

[TestFixture]
public class Zx81ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Zx81Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Zx81Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Zx81Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Zx81Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[793];
    var result = Zx81Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Zx81Reader.FromStream(null!));
}

[TestFixture]
public class Zx81WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Zx81Writer.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is793() {
    var file = new Zx81File { PixelData = new byte[793] };
    var bytes = Zx81Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(793));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[793];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new Zx81File { PixelData = data };
    var bytes = Zx81Writer.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[793];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = Zx81Reader.FromBytes(original);
    var written = Zx81Writer.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[793];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = Zx81Reader.FromFile(new FileInfo(tmp));
      var written = Zx81Writer.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is256()
    => Assert.That(Zx81File.FixedWidth, Is.EqualTo(256));

  [Test]
  public void FixedHeight_Is192()
    => Assert.That(Zx81File.FixedHeight, Is.EqualTo(192));

  [Test]
  public void FileSize_Is793()
    => Assert.That(Zx81File.FileSize, Is.EqualTo(793));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Zx81File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Zx81File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<ArgumentException>(() => Zx81File.FromRawImage(raw));
  }
}
