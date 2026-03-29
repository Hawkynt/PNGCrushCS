using System;
using System.IO;
using FileFormat.Vector06c;
using FileFormat.Core;

namespace FileFormat.Vector06c.Tests;

[TestFixture]
public class Vector06cReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vector06cReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Vector06cReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vector06cReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Vector06cReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[16384];
    var result = Vector06cReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(256));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vector06cReader.FromStream(null!));
}

[TestFixture]
public class Vector06cWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vector06cWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is16384() {
    var file = new Vector06cFile { PixelData = new byte[16384] };
    var bytes = Vector06cWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new Vector06cFile { PixelData = data };
    var bytes = Vector06cWriter.ToBytes(file);
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
    var file = Vector06cReader.FromBytes(original);
    var written = Vector06cWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[16384];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = Vector06cReader.FromFile(new FileInfo(tmp));
      var written = Vector06cWriter.ToBytes(file);
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
    => Assert.That(Vector06cFile.FixedWidth, Is.EqualTo(256));

  [Test]
  public void FixedHeight_Is256()
    => Assert.That(Vector06cFile.FixedHeight, Is.EqualTo(256));

  [Test]
  public void FileSize_Is16384()
    => Assert.That(Vector06cFile.FileSize, Is.EqualTo(16384));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vector06cFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vector06cFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 256, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 256 * 3] };
    Assert.Throws<ArgumentException>(() => Vector06cFile.FromRawImage(raw));
  }
}
