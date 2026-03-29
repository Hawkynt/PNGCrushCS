using System;
using System.IO;
using FileFormat.Dragon;
using FileFormat.Core;

namespace FileFormat.Dragon.Tests;

[TestFixture]
public class DragonReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DragonReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => DragonReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DragonReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => DragonReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[6144];
    var result = DragonReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DragonReader.FromStream(null!));
}

[TestFixture]
public class DragonWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DragonWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is6144() {
    var file = new DragonFile { PixelData = new byte[6144] };
    var bytes = DragonWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(6144));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[6144];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new DragonFile { PixelData = data };
    var bytes = DragonWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[6144];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = DragonReader.FromBytes(original);
    var written = DragonWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[6144];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = DragonReader.FromFile(new FileInfo(tmp));
      var written = DragonWriter.ToBytes(file);
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
    => Assert.That(DragonFile.FixedWidth, Is.EqualTo(256));

  [Test]
  public void FixedHeight_Is192()
    => Assert.That(DragonFile.FixedHeight, Is.EqualTo(192));

  [Test]
  public void FileSize_Is6144()
    => Assert.That(DragonFile.FileSize, Is.EqualTo(6144));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DragonFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DragonFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<ArgumentException>(() => DragonFile.FromRawImage(raw));
  }
}
