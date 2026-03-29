using System;
using System.IO;
using FileFormat.PixelPerfect;
using FileFormat.Core;

namespace FileFormat.PixelPerfect.Tests;

[TestFixture]
public sealed class PixelPerfectReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pp"));
    Assert.Throws<FileNotFoundException>(() => PixelPerfectReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PixelPerfectReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactlyMinSize_DoesNotThrow() {
    var data = new byte[PixelPerfectFile.LoadAddressSize + PixelPerfectFile.MinBitmapSize];
    data[0] = 0x00;
    data[1] = 0x60;
    Assert.DoesNotThrow(() => PixelPerfectReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var result = PixelPerfectReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.RawData.Length, Is.EqualTo(PixelPerfectFile.StandardPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_LittleEndian() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x4000);
    var result = PixelPerfectReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawData_ExcludesLoadAddress() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var result = PixelPerfectReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(data.Length - PixelPerfectFile.LoadAddressSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    using var ms = new MemoryStream(data);
    var result = PixelPerfectReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.RawData.Length, Is.EqualTo(PixelPerfectFile.StandardPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataCopied_NotShared() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var result = PixelPerfectReader.FromBytes(data);

    data[PixelPerfectFile.LoadAddressSize] = 0xFF;
    Assert.That(result.RawData[0], Is.Not.EqualTo(0xFF));
  }
}

[TestFixture]
public sealed class PixelPerfectWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var bytes = PixelPerfectWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(PixelPerfectFile.LoadAddressSize + PixelPerfectFile.StandardPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_LittleEndian() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var bytes = PixelPerfectWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x60));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PayloadData_FollowsLoadAddress() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    rawData[0] = 0xAB;
    rawData[1] = 0xCD;
    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var bytes = PixelPerfectWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAB));
    Assert.That(bytes[3], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StandardFileSize_Is9002() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var bytes = PixelPerfectWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(9002));
  }
}

[TestFixture]
public sealed class PixelPerfectRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };

    var bytes = PixelPerfectWriter.ToBytes(original);
    var restored = PixelPerfectReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pp");
    try {
      File.WriteAllBytes(tempPath, PixelPerfectWriter.ToBytes(original));
      var restored = PixelPerfectReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    var original = new PixelPerfectFile { LoadAddress = 0x4000, RawData = rawData };

    var bytes = PixelPerfectWriter.ToBytes(original);
    var restored = PixelPerfectReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    Array.Fill(rawData, (byte)0xFF);

    var original = new PixelPerfectFile { LoadAddress = 0xFFFF, RawData = rawData };

    var bytes = PixelPerfectWriter.ToBytes(original);
    var restored = PixelPerfectReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_ProducesValidOutput() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var image = PixelPerfectFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(PixelPerfectFile.FixedWidth));
    Assert.That(image.Height, Is.EqualTo(PixelPerfectFile.FixedHeight));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }
}

[TestFixture]
public sealed class PixelPerfectDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsPp() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".pp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsPpAndPpp() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".pp"));
    Assert.That(extensions, Does.Contain(".ppp"));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(PixelPerfectFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(PixelPerfectFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MinBitmapSize_Is8000() {
    Assert.That(PixelPerfectFile.MinBitmapSize, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void ScreenRamSize_Is1000() {
    Assert.That(PixelPerfectFile.ScreenRamSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void StandardPayloadSize_Is9000() {
    Assert.That(PixelPerfectFile.StandardPayloadSize, Is.EqualTo(9000));
  }

  [Test]
  [Category("Unit")]
  public void LoadAddressSize_Is2() {
    Assert.That(PixelPerfectFile.LoadAddressSize, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => PixelPerfectFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void DefaultRawData_IsEmptyArray() {
    var file = new PixelPerfectFile();
    Assert.That(file.RawData, Is.Not.Null);
    Assert.That(file.RawData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DefaultLoadAddress_IsZero() {
    var file = new PixelPerfectFile();
    Assert.That(file.LoadAddress, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Width_Always160() {
    var file = new PixelPerfectFile();
    Assert.That(file.Width, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void Height_Always200() {
    var file = new PixelPerfectFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_OutputFormat_IsRgb24() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var file = PixelPerfectReader.FromBytes(data);
    var image = PixelPerfectFile.ToRawImage(file);

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_OutputDimensions_Match() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var file = PixelPerfectReader.FromBytes(data);
    var image = PixelPerfectFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(160));
    Assert.That(image.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AllZeroPayload_ProducesBlackPixels() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var image = PixelPerfectFile.ToRawImage(file);

    for (var i = 0; i < image.PixelData.Length; ++i)
      Assert.That(image.PixelData[i], Is.EqualTo(0), $"Non-black pixel at byte offset {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_BitmapOnlyPayload_ProducesBinaryOutput() {
    var rawData = new byte[PixelPerfectFile.MinBitmapSize];
    rawData[0] = 0xFF;
    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var image = PixelPerfectFile.ToRawImage(file);

    Assert.That(image.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(image.PixelData[1], Is.EqualTo(0xFF));
    Assert.That(image.PixelData[2], Is.EqualTo(0xFF));
  }

  private static string _GetPrimaryExtension() => _Helper<PixelPerfectFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<PixelPerfectFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidPixelPerfectData(ushort loadAddress) {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[PixelPerfectFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, PixelPerfectFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
