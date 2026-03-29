using System;
using System.IO;
using FileFormat.DoomFlat;
using FileFormat.Core;

namespace FileFormat.DoomFlat.Tests;

[TestFixture]
public class DoomFlatReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DoomFlatReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => DoomFlatReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DoomFlatReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => DoomFlatReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[4096];
    var result = DoomFlatReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(64));
    Assert.That(result.Height, Is.EqualTo(64));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DoomFlatReader.FromStream(null!));
}

[TestFixture]
public class DoomFlatWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DoomFlatWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is4096() {
    var file = new DoomFlatFile { PixelData = new byte[4096] };
    var bytes = DoomFlatWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(4096));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[4096];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new DoomFlatFile { PixelData = data };
    var bytes = DoomFlatWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[4096];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = DoomFlatReader.FromBytes(original);
    var written = DoomFlatWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[4096];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = DoomFlatReader.FromFile(new FileInfo(tmp));
      var written = DoomFlatWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is64()
    => Assert.That(DoomFlatFile.FixedWidth, Is.EqualTo(64));

  [Test]
  public void FixedHeight_Is64()
    => Assert.That(DoomFlatFile.FixedHeight, Is.EqualTo(64));

  [Test]
  public void FileSize_Is4096()
    => Assert.That(DoomFlatFile.FileSize, Is.EqualTo(4096));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DoomFlatFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DoomFlatFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 64, Height = 64, Format = PixelFormat.Rgb24, PixelData = new byte[64 * 64 * 3] };
    Assert.Throws<ArgumentException>(() => DoomFlatFile.FromRawImage(raw));
  }
}
