using System;
using System.IO;
using FileFormat.ZxTricolor;
using FileFormat.Core;

namespace FileFormat.ZxTricolor.Tests;

[TestFixture]
public class ZxTricolorReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxTricolorReader.FromFile(new FileInfo("nonexistent.3cl")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxTricolorReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxTricolorReader.FromBytes(new byte[21000]));

  [Test]
  public void FromBytes_BitmapData1_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.BitmapData1.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_BitmapData2_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.BitmapData2.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_BitmapData3_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.BitmapData3.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData1_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.AttributeData1.Length, Is.EqualTo(768));
  }

  [Test]
  public void FromBytes_AttributeData2_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.AttributeData2.Length, Is.EqualTo(768));
  }

  [Test]
  public void FromBytes_AttributeData3_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.AttributeData3.Length, Is.EqualTo(768));
  }
}

[TestFixture]
public class ZxTricolorWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is20736() {
    var file = new ZxTricolorFile {
      BitmapData1 = new byte[6144],
      AttributeData1 = new byte[768],
      BitmapData2 = new byte[6144],
      AttributeData2 = new byte[768],
      BitmapData3 = new byte[6144],
      AttributeData3 = new byte[768],
    };
    var bytes = ZxTricolorWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(20736));
  }

  [Test]
  public void ToBytes_AttributeData_Preserved() {
    var attr1 = new byte[768];
    var attr2 = new byte[768];
    var attr3 = new byte[768];
    for (var i = 0; i < 768; ++i) {
      attr1[i] = (byte)(i & 0xFF);
      attr2[i] = (byte)((i * 3) & 0xFF);
      attr3[i] = (byte)((i * 5) & 0xFF);
    }
    var file = new ZxTricolorFile {
      BitmapData1 = new byte[6144],
      AttributeData1 = attr1,
      BitmapData2 = new byte[6144],
      AttributeData2 = attr2,
      BitmapData3 = new byte[6144],
      AttributeData3 = attr3,
    };
    var bytes = ZxTricolorWriter.ToBytes(file);
    var result = ZxTricolorReader.FromBytes(bytes);
    Assert.That(result.AttributeData1, Is.EqualTo(attr1));
    Assert.That(result.AttributeData2, Is.EqualTo(attr2));
    Assert.That(result.AttributeData3, Is.EqualTo(attr3));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[20736];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxTricolorReader.FromBytes(original);
    var written = ZxTricolorWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[20736];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxTricolorReader.FromFile(new FileInfo(tmp));
      var written = ZxTricolorWriter.ToBytes(file);
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
    var file = new ZxTricolorFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxTricolorFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void BitmapData1_DefaultIsEmpty() {
    var file = new ZxTricolorFile();
    Assert.That(file.BitmapData1, Is.Empty);
  }

  [Test]
  public void BitmapData2_DefaultIsEmpty() {
    var file = new ZxTricolorFile();
    Assert.That(file.BitmapData2, Is.Empty);
  }

  [Test]
  public void BitmapData3_DefaultIsEmpty() {
    var file = new ZxTricolorFile();
    Assert.That(file.BitmapData3, Is.Empty);
  }

  [Test]
  public void AttributeData1_DefaultIsEmpty() {
    var file = new ZxTricolorFile();
    Assert.That(file.AttributeData1, Is.Empty);
  }

  [Test]
  public void AttributeData2_DefaultIsEmpty() {
    var file = new ZxTricolorFile();
    Assert.That(file.AttributeData2, Is.Empty);
  }

  [Test]
  public void AttributeData3_DefaultIsEmpty() {
    var file = new ZxTricolorFile();
    Assert.That(file.AttributeData3, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxTricolorFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsRgb24() {
    var file = new ZxTricolorFile {
      BitmapData1 = new byte[6144],
      AttributeData1 = new byte[768],
      BitmapData2 = new byte[6144],
      AttributeData2 = new byte[768],
      BitmapData3 = new byte[6144],
      AttributeData3 = new byte[768],
    };
    var raw = ZxTricolorFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }
}
