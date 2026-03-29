using System;
using System.IO;
using FileFormat.ZxUlaPlus;
using FileFormat.Core;

namespace FileFormat.ZxUlaPlus.Tests;

[TestFixture]
public class ZxUlaPlusReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxUlaPlusReader.FromFile(new FileInfo("nonexistent.ulp")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxUlaPlusReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxUlaPlusReader.FromBytes(new byte[7000]));

  [Test]
  public void FromBytes_BitmapData_HasCorrectLength() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData_HasCorrectLength() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
  }

  [Test]
  public void FromBytes_PaletteData_HasCorrectLength() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.PaletteData.Length, Is.EqualTo(64));
  }

  [Test]
  public void FromBytes_PaletteData_IsCopied() {
    var data = new byte[6976];
    data[6912] = 0xAB;
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.PaletteData[0], Is.EqualTo(0xAB));
    data[6912] = 0x00;
    Assert.That(result.PaletteData[0], Is.EqualTo(0xAB));
  }
}

[TestFixture]
public class ZxUlaPlusWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is6976() {
    var file = new ZxUlaPlusFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      PaletteData = new byte[64],
    };
    var bytes = ZxUlaPlusWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(6976));
  }

  [Test]
  public void ToBytes_PaletteData_Preserved() {
    var palette = new byte[64];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 4);
    var file = new ZxUlaPlusFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      PaletteData = palette,
    };
    var bytes = ZxUlaPlusWriter.ToBytes(file);
    var result = ZxUlaPlusReader.FromBytes(bytes);
    Assert.That(result.PaletteData, Is.EqualTo(palette));
  }

  [Test]
  public void ToBytes_AttributeData_Preserved() {
    var attr = new byte[768];
    for (var i = 0; i < attr.Length; ++i)
      attr[i] = (byte)(i & 0xFF);
    var file = new ZxUlaPlusFile {
      BitmapData = new byte[6144],
      AttributeData = attr,
      PaletteData = new byte[64],
    };
    var bytes = ZxUlaPlusWriter.ToBytes(file);
    var result = ZxUlaPlusReader.FromBytes(bytes);
    Assert.That(result.AttributeData, Is.EqualTo(attr));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[6976];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxUlaPlusReader.FromBytes(original);
    var written = ZxUlaPlusWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[6976];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxUlaPlusReader.FromFile(new FileInfo(tmp));
      var written = ZxUlaPlusWriter.ToBytes(file);
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
    var file = new ZxUlaPlusFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxUlaPlusFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void BitmapData_DefaultIsEmpty() {
    var file = new ZxUlaPlusFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  public void AttributeData_DefaultIsEmpty() {
    var file = new ZxUlaPlusFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  public void PaletteData_DefaultIsEmpty() {
    var file = new ZxUlaPlusFile();
    Assert.That(file.PaletteData, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxUlaPlusFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsRgb24() {
    var file = new ZxUlaPlusFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      PaletteData = new byte[64],
    };
    var raw = ZxUlaPlusFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }
}
