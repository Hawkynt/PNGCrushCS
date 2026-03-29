using System;
using System.IO;
using FileFormat.Core;
using FileFormat.YuvRaw;

namespace FileFormat.YuvRaw.Tests;

[TestFixture]
public sealed class YuvRawReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => YuvRawReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yuv"));
    Assert.Throws<FileNotFoundException>(() => YuvRawReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => YuvRawReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => YuvRawReader.FromBytes(new byte[3]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Qcif_Parses() {
    // QCIF: 176x144, file size = 176*144*3/2 = 38016
    var data = new byte[38016];
    data[0] = 0xAB;

    var result = YuvRawReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(176));
    Assert.That(result.Height, Is.EqualTo(144));
    Assert.That(result.YPlane[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithDimensions_Parses() {
    var data = new byte[48]; // 4x4 * 3/2 = 24... actually 4*4 + 2*2 + 2*2 = 24
    data[0] = 128;

    var result = YuvRawReader.FromBytes(data, 4, 4);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.YPlane.Length, Is.EqualTo(16));
    Assert.That(result.UPlane.Length, Is.EqualTo(4));
    Assert.That(result.VPlane.Length, Is.EqualTo(4));
  }
}

[TestFixture]
public sealed class YuvRawWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => YuvRawWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesCorrectSize() {
    var file = new YuvRawFile {
      Width = 4, Height = 4,
      YPlane = new byte[16],
      UPlane = new byte[4],
      VPlane = new byte[4],
    };

    var bytes = YuvRawWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(24));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenRead_PreservesPlanes() {
    var yPlane = new byte[16];
    var uPlane = new byte[4];
    var vPlane = new byte[4];
    for (var i = 0; i < 16; ++i)
      yPlane[i] = (byte)(i * 10);
    uPlane[0] = 100; uPlane[1] = 200;
    vPlane[0] = 50; vPlane[1] = 150;

    var original = new YuvRawFile { Width = 4, Height = 4, YPlane = yPlane, UPlane = uPlane, VPlane = vPlane };

    var bytes = YuvRawWriter.ToBytes(original);
    var restored = YuvRawReader.FromBytes(bytes, 4, 4);

    Assert.That(restored.YPlane, Is.EqualTo(original.YPlane));
    Assert.That(restored.UPlane, Is.EqualTo(original.UPlane));
    Assert.That(restored.VPlane, Is.EqualTo(original.VPlane));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var file = new YuvRawFile {
      Width = 4, Height = 4,
      YPlane = new byte[16],
      UPlane = new byte[4],
      VPlane = new byte[4],
    };
    file.YPlane[0] = 0xFF;

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yuv");
    try {
      File.WriteAllBytes(tempPath, YuvRawWriter.ToBytes(file));
      var restored = YuvRawReader.FromBytes(File.ReadAllBytes(tempPath), 4, 4);

      Assert.That(restored.YPlane, Is.EqualTo(file.YPlane));
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
  public void YuvRawFile_Defaults() {
    var file = new YuvRawFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.YPlane, Is.Empty);
    Assert.That(file.UPlane, Is.Empty);
    Assert.That(file.VPlane, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void YuvRawFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => YuvRawFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void YuvRawFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => YuvRawFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void YuvRawFile_ToRawImage_ReturnsRgb24() {
    var file = new YuvRawFile {
      Width = 4, Height = 4,
      YPlane = new byte[16],
      UPlane = new byte[4],
      VPlane = new byte[4],
    };
    var raw = YuvRawFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void YuvRawFile_FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 4, Height = 4,
      Format = PixelFormat.Gray8,
      PixelData = new byte[16],
    };
    Assert.Throws<ArgumentException>(() => YuvRawFile.FromRawImage(raw));
  }
}
