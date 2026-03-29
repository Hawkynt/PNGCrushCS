using System;
using System.IO;
using FileFormat.ZxGigascreen;
using FileFormat.Core;

namespace FileFormat.ZxGigascreen.Tests;

[TestFixture]
public class ZxGigascreenReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxGigascreenReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxGigascreenReader.FromFile(new FileInfo("nonexistent.gsc")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxGigascreenReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxGigascreenReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[13824];
    var result = ZxGigascreenReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxGigascreenReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxGigascreenReader.FromBytes(new byte[14000]));

  [Test]
  public void FromBytes_BitmapData1_HasCorrectLength() {
    var data = new byte[13824];
    var result = ZxGigascreenReader.FromBytes(data);
    Assert.That(result.BitmapData1.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_BitmapData2_HasCorrectLength() {
    var data = new byte[13824];
    var result = ZxGigascreenReader.FromBytes(data);
    Assert.That(result.BitmapData2.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData1_HasCorrectLength() {
    var data = new byte[13824];
    var result = ZxGigascreenReader.FromBytes(data);
    Assert.That(result.AttributeData1.Length, Is.EqualTo(768));
  }

  [Test]
  public void FromBytes_AttributeData2_HasCorrectLength() {
    var data = new byte[13824];
    var result = ZxGigascreenReader.FromBytes(data);
    Assert.That(result.AttributeData2.Length, Is.EqualTo(768));
  }
}

[TestFixture]
public class ZxGigascreenWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxGigascreenWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is13824() {
    var file = new ZxGigascreenFile {
      BitmapData1 = new byte[6144],
      AttributeData1 = new byte[768],
      BitmapData2 = new byte[6144],
      AttributeData2 = new byte[768],
    };
    var bytes = ZxGigascreenWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(13824));
  }

  [Test]
  public void ToBytes_AttributeData_Preserved() {
    var attr1 = new byte[768];
    var attr2 = new byte[768];
    for (var i = 0; i < 768; ++i) {
      attr1[i] = (byte)(i & 0xFF);
      attr2[i] = (byte)((i * 3) & 0xFF);
    }
    var file = new ZxGigascreenFile {
      BitmapData1 = new byte[6144],
      AttributeData1 = attr1,
      BitmapData2 = new byte[6144],
      AttributeData2 = attr2,
    };
    var bytes = ZxGigascreenWriter.ToBytes(file);
    var result = ZxGigascreenReader.FromBytes(bytes);
    Assert.That(result.AttributeData1, Is.EqualTo(attr1));
    Assert.That(result.AttributeData2, Is.EqualTo(attr2));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[13824];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxGigascreenReader.FromBytes(original);
    var written = ZxGigascreenWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[13824];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxGigascreenReader.FromFile(new FileInfo(tmp));
      var written = ZxGigascreenWriter.ToBytes(file);
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
    var file = new ZxGigascreenFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxGigascreenFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void BitmapData1_DefaultIsEmpty() {
    var file = new ZxGigascreenFile();
    Assert.That(file.BitmapData1, Is.Empty);
  }

  [Test]
  public void BitmapData2_DefaultIsEmpty() {
    var file = new ZxGigascreenFile();
    Assert.That(file.BitmapData2, Is.Empty);
  }

  [Test]
  public void AttributeData1_DefaultIsEmpty() {
    var file = new ZxGigascreenFile();
    Assert.That(file.AttributeData1, Is.Empty);
  }

  [Test]
  public void AttributeData2_DefaultIsEmpty() {
    var file = new ZxGigascreenFile();
    Assert.That(file.AttributeData2, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxGigascreenFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxGigascreenFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxGigascreenFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsRgb24() {
    var file = new ZxGigascreenFile {
      BitmapData1 = new byte[6144],
      AttributeData1 = new byte[768],
      BitmapData2 = new byte[6144],
      AttributeData2 = new byte[768],
    };
    var raw = ZxGigascreenFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }
}
