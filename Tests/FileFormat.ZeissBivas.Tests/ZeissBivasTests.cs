using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZeissBivas;

namespace FileFormat.ZeissBivas.Tests;

[TestFixture]
public sealed class ZeissBivasReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissBivasReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dta"));
    Assert.Throws<FileNotFoundException>(() => ZeissBivasReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissBivasReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => ZeissBivasReader.FromBytes(new byte[8]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_Parses() {
    var data = new byte[20];
    data[0] = 4; data[1] = 0; data[2] = 0; data[3] = 0; // width = 4
    data[4] = 2; data[5] = 0; data[6] = 0; data[7] = 0; // height = 2
    data[8] = 8; data[9] = 0; data[10] = 0; data[11] = 0; // bpp = 8
    data[12] = 0xAB; // first pixel

    var result = ZeissBivasReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }
}

[TestFixture]
public sealed class ZeissBivasWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissBivasWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasCorrectHeader() {
    var file = new ZeissBivasFile { Width = 4, Height = 2, BitsPerPixel = 8, PixelData = new byte[8] };
    var bytes = ZeissBivasWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(20)); // 12 header + 8 pixels
    Assert.That(bytes[0], Is.EqualTo(4)); // width LE
    Assert.That(bytes[4], Is.EqualTo(2)); // height LE
    Assert.That(bytes[8], Is.EqualTo(8)); // bpp LE
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesData() {
    var pixelData = new byte[8];
    for (var i = 0; i < 8; ++i)
      pixelData[i] = (byte)(i * 30);

    var original = new ZeissBivasFile { Width = 4, Height = 2, BitsPerPixel = 8, PixelData = pixelData };

    var bytes = ZeissBivasWriter.ToBytes(original);
    var restored = ZeissBivasReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
    var original = new ZeissBivasFile { Width = 4, Height = 2, BitsPerPixel = 8, PixelData = pixelData };

    var raw = ZeissBivasFile.ToRawImage(original);
    var restored = ZeissBivasFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var original = new ZeissBivasFile { Width = 2, Height = 2, BitsPerPixel = 8, PixelData = pixelData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dta");
    try {
      File.WriteAllBytes(tempPath, ZeissBivasWriter.ToBytes(original));
      var restored = ZeissBivasReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ZeissBivasFile_Defaults() {
    var file = new ZeissBivasFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZeissBivasFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissBivasFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ZeissBivasFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZeissBivasFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ZeissBivasFile_FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 4, Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[24],
    };
    Assert.Throws<ArgumentException>(() => ZeissBivasFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ZeissBivasFile_ToRawImage_ReturnsGray8() {
    var file = new ZeissBivasFile { Width = 2, Height = 2, BitsPerPixel = 8, PixelData = new byte[4] };
    var raw = ZeissBivasFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }
}
