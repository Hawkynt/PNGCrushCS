using System;
using System.IO;
using FileFormat.CreateWithGarfield;
using FileFormat.Core;

namespace FileFormat.CreateWithGarfield.Tests;

[TestFixture]
public sealed class CreateWithGarfieldReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CreateWithGarfieldReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CreateWithGarfieldReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cwg"));
    Assert.Throws<FileNotFoundException>(() => CreateWithGarfieldReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CreateWithGarfieldReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => CreateWithGarfieldReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => CreateWithGarfieldReader.FromBytes(new byte[9004]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[CreateWithGarfieldFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60; // 0x6000 LE

    var result = CreateWithGarfieldReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[CreateWithGarfieldFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60;

    var result = CreateWithGarfieldReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapData_CopiedCorrectly() {
    var data = new byte[CreateWithGarfieldFile.ExpectedFileSize];
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = CreateWithGarfieldReader.FromBytes(data);

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BorderColor_ParsedCorrectly() {
    var data = new byte[CreateWithGarfieldFile.ExpectedFileSize];
    data[9002] = 0x06; // border color at offset 2+8000+1000

    var result = CreateWithGarfieldReader.FromBytes(data);

    Assert.That(result.BorderColor, Is.EqualTo(0x06));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[CreateWithGarfieldFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60;

    using var ms = new MemoryStream(data);
    var result = CreateWithGarfieldReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }
}

[TestFixture]
public sealed class CreateWithGarfieldWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CreateWithGarfieldWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly9003Bytes() {
    var file = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CreateWithGarfieldFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x60));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataOffset_StartsAtByte2() {
    var file = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0,
    };
    file.BitmapData[0] = 0xAA;
    file.BitmapData[7999] = 0xBB;

    var bytes = CreateWithGarfieldWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenRamOffset_StartsAtByte8002() {
    var file = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0,
    };
    file.ScreenRam[0] = 0xCC;
    file.ScreenRam[999] = 0xDD;

    var bytes = CreateWithGarfieldWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BorderColor_WrittenAtCorrectOffset() {
    var file = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0x0E,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0x0E));
  }
}

[TestFixture]
public sealed class CreateWithGarfieldRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 256);

    var original = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      BorderColor = 0x06,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(original);
    var restored = CreateWithGarfieldReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.BorderColor, Is.EqualTo(original.BorderColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new CreateWithGarfieldFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(original);
    var restored = CreateWithGarfieldReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmapData = new byte[8000];
    Array.Fill(bitmapData, (byte)0xFF);

    var screenRam = new byte[1000];
    Array.Fill(screenRam, (byte)0xFF);

    var original = new CreateWithGarfieldFile {
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      BorderColor = 0x0F,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(original);
    var restored = CreateWithGarfieldReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.BorderColor, Is.EqualTo(0x0F));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(original);
    var restored = CreateWithGarfieldReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BorderColorPreserved() {
    var original = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0x0E,
    };

    var bytes = CreateWithGarfieldWriter.ToBytes(original);
    var restored = CreateWithGarfieldReader.FromBytes(bytes);

    Assert.That(restored.BorderColor, Is.EqualTo(0x0E));
  }
}

[TestFixture]
public sealed class CreateWithGarfieldDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsCwg() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".cwg"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsCwg() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".cwg"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CreateWithGarfieldFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CreateWithGarfieldFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => CreateWithGarfieldFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidFile_ReturnsRgb24() {
    var file = new CreateWithGarfieldFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      BorderColor = 0,
    };

    var image = CreateWithGarfieldFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(320));
    Assert.That(image.Height, Is.EqualTo(200));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is320() {
    Assert.That(CreateWithGarfieldFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(CreateWithGarfieldFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ExpectedFileSize_Is9003() {
    Assert.That(CreateWithGarfieldFile.ExpectedFileSize, Is.EqualTo(9003));
  }

  private static string _GetPrimaryExtension() => _Helper<CreateWithGarfieldFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<CreateWithGarfieldFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}
