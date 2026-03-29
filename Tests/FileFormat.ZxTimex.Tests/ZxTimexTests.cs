using System;
using System.IO;
using FileFormat.ZxTimex;
using FileFormat.Core;

namespace FileFormat.ZxTimex.Tests;

[TestFixture]
public class ZxTimexReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTimexReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxTimexReader.FromFile(new FileInfo("nonexistent.tmx")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTimexReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxTimexReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[12288];
    var result = ZxTimexReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTimexReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxTimexReader.FromBytes(new byte[13000]));

  [Test]
  public void FromBytes_BitmapData_HasCorrectLength() {
    var data = new byte[12288];
    var result = ZxTimexReader.FromBytes(data);
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData_HasCorrectLength() {
    var data = new byte[12288];
    var result = ZxTimexReader.FromBytes(data);
    Assert.That(result.AttributeData.Length, Is.EqualTo(6144));
  }
}

[TestFixture]
public class ZxTimexWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTimexWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is12288() {
    var file = new ZxTimexFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[6144],
    };
    var bytes = ZxTimexWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(12288));
  }

  [Test]
  public void ToBytes_AttributeData_Preserved() {
    var attr = new byte[6144];
    for (var i = 0; i < attr.Length; ++i)
      attr[i] = (byte)(i & 0xFF);
    var file = new ZxTimexFile {
      BitmapData = new byte[6144],
      AttributeData = attr,
    };
    var bytes = ZxTimexWriter.ToBytes(file);
    var result = ZxTimexReader.FromBytes(bytes);
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
    var file = ZxTimexReader.FromBytes(original);
    var written = ZxTimexWriter.ToBytes(file);
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
      var file = ZxTimexReader.FromFile(new FileInfo(tmp));
      var written = ZxTimexWriter.ToBytes(file);
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
    var file = new ZxTimexFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxTimexFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void BitmapData_DefaultIsEmpty() {
    var file = new ZxTimexFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  public void AttributeData_DefaultIsEmpty() {
    var file = new ZxTimexFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTimexFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTimexFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxTimexFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsRgb24() {
    var file = new ZxTimexFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[6144],
    };
    var raw = ZxTimexFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }
}
