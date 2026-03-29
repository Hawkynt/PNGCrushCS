using System;
using System.IO;
using FileFormat.ZxFlash;
using FileFormat.Core;

namespace FileFormat.ZxFlash.Tests;

[TestFixture]
public class ZxFlashReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxFlashReader.FromFile(new FileInfo("nonexistent.zfl")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxFlashReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashReader.FromStream(null!));

  [Test]
  public void FromBytes_SingleFrame_FrameCountIs1() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.FrameCount, Is.EqualTo(1));
  }

  [Test]
  public void FromBytes_TwoFrames_FrameCountIs2() {
    var data = new byte[6912 * 2];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.FrameCount, Is.EqualTo(2));
  }

  [Test]
  public void FromBytes_BitmapData_HasCorrectLength() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData_HasCorrectLength() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
  }
}

[TestFixture]
public class ZxFlashWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is6912() {
    var file = new ZxFlashFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
    };
    var bytes = ZxFlashWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(6912));
  }

  [Test]
  public void ToBytes_AttributeData_Preserved() {
    var attr = new byte[768];
    for (var i = 0; i < attr.Length; ++i)
      attr[i] = (byte)(i & 0xFF);
    var file = new ZxFlashFile {
      BitmapData = new byte[6144],
      AttributeData = attr,
    };
    var bytes = ZxFlashWriter.ToBytes(file);
    var result = ZxFlashReader.FromBytes(bytes);
    Assert.That(result.AttributeData, Is.EqualTo(attr));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_SingleFrame_Preserved() {
    var original = new byte[6912];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxFlashReader.FromBytes(original);
    var written = ZxFlashWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[6912];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxFlashReader.FromFile(new FileInfo(tmp));
      var written = ZxFlashWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void Width_Is256() {
    var file = new ZxFlashFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxFlashFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void FrameCount_DefaultIs1() {
    var file = new ZxFlashFile();
    Assert.That(file.FrameCount, Is.EqualTo(1));
  }

  [Test]
  public void BitmapData_DefaultIsEmpty() {
    var file = new ZxFlashFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  public void AttributeData_DefaultIsEmpty() {
    var file = new ZxFlashFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxFlashFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsRgb24() {
    var file = new ZxFlashFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
    };
    var raw = ZxFlashFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }
}
