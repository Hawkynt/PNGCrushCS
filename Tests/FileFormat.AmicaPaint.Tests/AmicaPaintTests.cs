using System;
using System.IO;
using FileFormat.AmicaPaint;
using FileFormat.Core;

namespace FileFormat.AmicaPaint.Tests;

[TestFixture]
public sealed class AmicaPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmicaPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmicaPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ami"));
    Assert.Throws<FileNotFoundException>(() => AmicaPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmicaPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => AmicaPaintReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => AmicaPaintReader.FromBytes(new byte[10004]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[AmicaPaintFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x40; // 0x4000 LE

    var result = AmicaPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[AmicaPaintFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x40;

    var result = AmicaPaintReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapData_CopiedCorrectly() {
    var data = new byte[AmicaPaintFile.ExpectedFileSize];
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = AmicaPaintReader.FromBytes(data);

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ScreenRam_CopiedCorrectly() {
    var data = new byte[AmicaPaintFile.ExpectedFileSize];
    data[8002] = 0xEE;
    data[9001] = 0xFF;

    var result = AmicaPaintReader.FromBytes(data);

    Assert.That(result.ScreenRam[0], Is.EqualTo(0xEE));
    Assert.That(result.ScreenRam[999], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ColorRam_CopiedCorrectly() {
    var data = new byte[AmicaPaintFile.ExpectedFileSize];
    data[9002] = 0x11;
    data[10001] = 0x22;

    var result = AmicaPaintReader.FromBytes(data);

    Assert.That(result.ColorRam[0], Is.EqualTo(0x11));
    Assert.That(result.ColorRam[999], Is.EqualTo(0x22));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BackgroundColor_ParsedCorrectly() {
    var data = new byte[AmicaPaintFile.ExpectedFileSize];
    data[10002] = 0x06;

    var result = AmicaPaintReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0x06));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[AmicaPaintFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x40;

    using var ms = new MemoryStream(data);
    var result = AmicaPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }
}

[TestFixture]
public sealed class AmicaPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmicaPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly10003Bytes() {
    var file = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = AmicaPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(AmicaPaintFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = AmicaPaintWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataOffset_StartsAtByte2() {
    var file = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };
    file.BitmapData[0] = 0xAA;
    file.BitmapData[7999] = 0xBB;

    var bytes = AmicaPaintWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenRamOffset_StartsAtByte8002() {
    var file = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };
    file.ScreenRam[0] = 0xCC;
    file.ScreenRam[999] = 0xDD;

    var bytes = AmicaPaintWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorRamOffset_StartsAtByte9002() {
    var file = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };
    file.ColorRam[0] = 0x11;
    file.ColorRam[999] = 0x22;

    var bytes = AmicaPaintWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0x11));
    Assert.That(bytes[10001], Is.EqualTo(0x22));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BackgroundColor_WrittenAtCorrectOffset() {
    var file = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0x0E,
    };

    var bytes = AmicaPaintWriter.ToBytes(file);

    Assert.That(bytes[10002], Is.EqualTo(0x0E));
  }
}

[TestFixture]
public sealed class AmicaPaintRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 256);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)(i * 3 % 256);

    var original = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = 0x06,
    };

    var bytes = AmicaPaintWriter.ToBytes(original);
    var restored = AmicaPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new AmicaPaintFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = AmicaPaintWriter.ToBytes(original);
    var restored = AmicaPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmapData = new byte[8000];
    Array.Fill(bitmapData, (byte)0xFF);

    var screenRam = new byte[1000];
    Array.Fill(screenRam, (byte)0xFF);

    var colorRam = new byte[1000];
    Array.Fill(colorRam, (byte)0xFF);

    var original = new AmicaPaintFile {
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = 0x0F,
    };

    var bytes = AmicaPaintWriter.ToBytes(original);
    var restored = AmicaPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(0x0F));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = AmicaPaintWriter.ToBytes(original);
    var restored = AmicaPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BackgroundColorPreserved() {
    var original = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0x0E,
    };

    var bytes = AmicaPaintWriter.ToBytes(original);
    var restored = AmicaPaintReader.FromBytes(bytes);

    Assert.That(restored.BackgroundColor, Is.EqualTo(0x0E));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 13 % 256);

    var original = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0x03,
    };
    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ami");
    try {
      File.WriteAllBytes(tmpPath, AmicaPaintWriter.ToBytes(original));
      var restored = AmicaPaintReader.FromFile(new FileInfo(tmpPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }
}

[TestFixture]
public sealed class AmicaPaintDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsAmi() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".ami"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsAmi() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".ami"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmicaPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmicaPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => AmicaPaintFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidFile_ReturnsRgb24() {
    var file = new AmicaPaintFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };

    var image = AmicaPaintFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(160));
    Assert.That(image.Height, Is.EqualTo(200));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(AmicaPaintFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(AmicaPaintFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ExpectedFileSize_Is10003() {
    Assert.That(AmicaPaintFile.ExpectedFileSize, Is.EqualTo(10003));
  }

  [Test]
  [Category("Unit")]
  public void DefaultBitmapData_IsEmpty() {
    var file = new AmicaPaintFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultScreenRam_IsEmpty() {
    var file = new AmicaPaintFile();
    Assert.That(file.ScreenRam, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultColorRam_IsEmpty() {
    var file = new AmicaPaintFile();
    Assert.That(file.ColorRam, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<AmicaPaintFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<AmicaPaintFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}
