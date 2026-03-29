using System;
using System.IO;
using FileFormat.MonoMagic;
using FileFormat.Core;

namespace FileFormat.MonoMagic.Tests;

[TestFixture]
public class MonoMagicReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => MonoMagicReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MonoMagicReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[9009];
    var result = MonoMagicReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicReader.FromStream(null!));
}

[TestFixture]
public class MonoMagicWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is9009() {
    var file = new MonoMagicFile { PixelData = new byte[9009] };
    var bytes = MonoMagicWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(9009));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[9009];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new MonoMagicFile { PixelData = data };
    var bytes = MonoMagicWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[9009];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = MonoMagicReader.FromBytes(original);
    var written = MonoMagicWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[9009];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = MonoMagicReader.FromFile(new FileInfo(tmp));
      var written = MonoMagicWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is320()
    => Assert.That(MonoMagicFile.FixedWidth, Is.EqualTo(320));

  [Test]
  public void FixedHeight_Is200()
    => Assert.That(MonoMagicFile.FixedHeight, Is.EqualTo(200));

  [Test]
  public void FileSize_Is9009()
    => Assert.That(MonoMagicFile.FileSize, Is.EqualTo(9009));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<ArgumentException>(() => MonoMagicFile.FromRawImage(raw));
  }
}
