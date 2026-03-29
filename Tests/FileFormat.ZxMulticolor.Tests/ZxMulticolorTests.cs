using System;
using System.IO;
using FileFormat.ZxMulticolor;
using FileFormat.Core;

namespace FileFormat.ZxMulticolor.Tests;

[TestFixture]
public class ZxMulticolorReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMulticolorReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxMulticolorReader.FromFile(new FileInfo("nonexistent.mlt")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMulticolorReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxMulticolorReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[12288];
    var result = ZxMulticolorReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMulticolorReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxMulticolorReader.FromBytes(new byte[13000]));

  [Test]
  public void FromBytes_BitmapData_HasCorrectLength() {
    var data = new byte[12288];
    var result = ZxMulticolorReader.FromBytes(data);
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData_HasCorrectLength() {
    var data = new byte[12288];
    var result = ZxMulticolorReader.FromBytes(data);
    Assert.That(result.AttributeData.Length, Is.EqualTo(6144));
  }
}

[TestFixture]
public class ZxMulticolorWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMulticolorWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is12288() {
    var file = new ZxMulticolorFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[6144],
    };
    var bytes = ZxMulticolorWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(12288));
  }

  [Test]
  public void ToBytes_AttributeData_Preserved() {
    var attr = new byte[6144];
    for (var i = 0; i < attr.Length; ++i)
      attr[i] = (byte)(i & 0xFF);
    var file = new ZxMulticolorFile {
      BitmapData = new byte[6144],
      AttributeData = attr,
    };
    var bytes = ZxMulticolorWriter.ToBytes(file);
    var result = ZxMulticolorReader.FromBytes(bytes);
    Assert.That(result.AttributeData, Is.EqualTo(attr));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[12288];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxMulticolorReader.FromBytes(original);
    var written = ZxMulticolorWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[12288];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxMulticolorReader.FromFile(new FileInfo(tmp));
      var written = ZxMulticolorWriter.ToBytes(file);
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
    var file = new ZxMulticolorFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxMulticolorFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void BitmapData_DefaultIsEmpty() {
    var file = new ZxMulticolorFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  public void AttributeData_DefaultIsEmpty() {
    var file = new ZxMulticolorFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMulticolorFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxMulticolorFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxMulticolorFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsRgb24() {
    var file = new ZxMulticolorFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[6144],
    };
    var raw = ZxMulticolorFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }
}
