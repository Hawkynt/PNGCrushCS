using System;
using System.IO;
using FileFormat.C128;
using FileFormat.Core;

namespace FileFormat.C128.Tests;

[TestFixture]
public class C128ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C128Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => C128Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C128Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => C128Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[16384];
    var result = C128Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C128Reader.FromStream(null!));
}

[TestFixture]
public class C128WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C128Writer.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is16384() {
    var file = new C128File { PixelData = new byte[16384] };
    var bytes = C128Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new C128File { PixelData = data };
    var bytes = C128Writer.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[16384];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = C128Reader.FromBytes(original);
    var written = C128Writer.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[16384];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = C128Reader.FromFile(new FileInfo(tmp));
      var written = C128Writer.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is640()
    => Assert.That(C128File.FixedWidth, Is.EqualTo(640));

  [Test]
  public void FixedHeight_Is200()
    => Assert.That(C128File.FixedHeight, Is.EqualTo(200));

  [Test]
  public void FileSize_Is16384()
    => Assert.That(C128File.FileSize, Is.EqualTo(16384));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C128File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C128File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 640, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[640 * 200 * 3] };
    Assert.Throws<ArgumentException>(() => C128File.FromRawImage(raw));
  }
}
