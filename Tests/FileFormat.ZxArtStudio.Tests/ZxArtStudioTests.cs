using System;
using System.IO;
using FileFormat.ZxArtStudio;
using FileFormat.Core;

namespace FileFormat.ZxArtStudio.Tests;

[TestFixture]
public class ZxArtStudioReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxArtStudioReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxArtStudioReader.FromFile(new FileInfo("nonexistent.zas")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxArtStudioReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxArtStudioReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[6912];
    var result = ZxArtStudioReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxArtStudioReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxArtStudioReader.FromBytes(new byte[7000]));

  [Test]
  public void FromBytes_BitmapData_HasCorrectLength() {
    var data = new byte[6912];
    var result = ZxArtStudioReader.FromBytes(data);
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData_HasCorrectLength() {
    var data = new byte[6912];
    var result = ZxArtStudioReader.FromBytes(data);
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
  }
}

[TestFixture]
public class ZxArtStudioWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxArtStudioWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is6912() {
    var file = new ZxArtStudioFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
    };
    var bytes = ZxArtStudioWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(6912));
  }

  [Test]
  public void ToBytes_AttributeData_Preserved() {
    var attr = new byte[768];
    for (var i = 0; i < attr.Length; ++i)
      attr[i] = (byte)(i & 0xFF);
    var file = new ZxArtStudioFile {
      BitmapData = new byte[6144],
      AttributeData = attr,
    };
    var bytes = ZxArtStudioWriter.ToBytes(file);
    var result = ZxArtStudioReader.FromBytes(bytes);
    Assert.That(result.AttributeData, Is.EqualTo(attr));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[6912];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxArtStudioReader.FromBytes(original);
    var written = ZxArtStudioWriter.ToBytes(file);
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
      var file = ZxArtStudioReader.FromFile(new FileInfo(tmp));
      var written = ZxArtStudioWriter.ToBytes(file);
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
    var file = new ZxArtStudioFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxArtStudioFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void BitmapData_DefaultIsEmpty() {
    var file = new ZxArtStudioFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  public void AttributeData_DefaultIsEmpty() {
    var file = new ZxArtStudioFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxArtStudioFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxArtStudioFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxArtStudioFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsRgb24() {
    var file = new ZxArtStudioFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
    };
    var raw = ZxArtStudioFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }
}
