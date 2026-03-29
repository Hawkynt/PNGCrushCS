using System;
using System.IO;
using FileFormat.C16Plus4;
using FileFormat.Core;

namespace FileFormat.C16Plus4.Tests;

[TestFixture]
public class C16Plus4ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C16Plus4Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => C16Plus4Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C16Plus4Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => C16Plus4Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[10003];
    var result = C16Plus4Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C16Plus4Reader.FromStream(null!));
}

[TestFixture]
public class C16Plus4WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C16Plus4Writer.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is10003() {
    var file = new C16Plus4File { PixelData = new byte[10003] };
    var bytes = C16Plus4Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(10003));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[10003];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new C16Plus4File { PixelData = data };
    var bytes = C16Plus4Writer.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[10003];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = C16Plus4Reader.FromBytes(original);
    var written = C16Plus4Writer.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[10003];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = C16Plus4Reader.FromFile(new FileInfo(tmp));
      var written = C16Plus4Writer.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is160()
    => Assert.That(C16Plus4File.FixedWidth, Is.EqualTo(160));

  [Test]
  public void FixedHeight_Is200()
    => Assert.That(C16Plus4File.FixedHeight, Is.EqualTo(200));

  [Test]
  public void FileSize_Is10003()
    => Assert.That(C16Plus4File.FileSize, Is.EqualTo(10003));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C16Plus4File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => C16Plus4File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<ArgumentException>(() => C16Plus4File.FromRawImage(raw));
  }
}
