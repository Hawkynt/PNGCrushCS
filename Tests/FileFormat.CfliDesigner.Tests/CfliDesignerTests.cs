using System;
using System.IO;
using FileFormat.CfliDesigner;
using FileFormat.Core;

namespace FileFormat.CfliDesigner.Tests;

[TestFixture]
public sealed class CfliDesignerReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CfliDesignerReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cfli"));
    Assert.Throws<FileNotFoundException>(() => CfliDesignerReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CfliDesignerReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => CfliDesignerReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidCfliData(0x3C00);
    var result = CfliDesignerReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(17000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CfliDesignerReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidCfliData(0x3C00);
    using var ms = new MemoryStream(data);
    var result = CfliDesignerReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.EqualTo(17000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_LittleEndian() {
    var data = TestHelpers._BuildValidCfliData(0xABCD);
    var result = CfliDesignerReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0xABCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactMinSize_Succeeds() {
    var data = TestHelpers._BuildValidCfliData(0x3C00);
    Assert.That(data.Length, Is.EqualTo(2 + 17000));
    Assert.DoesNotThrow(() => CfliDesignerReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OneBelowMinSize_ThrowsInvalidDataException() {
    var data = new byte[2 + 17000 - 1];
    Assert.Throws<InvalidDataException>(() => CfliDesignerReader.FromBytes(data));
  }
}

[TestFixture]
public sealed class CfliDesignerWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CfliDesignerWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[17000];
    var file = new CfliDesignerFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = CfliDesignerWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 17000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_LittleEndian() {
    var file = new CfliDesignerFile { LoadAddress = 0xABCD, RawData = new byte[17000] };
    var bytes = CfliDesignerWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xCD));
    Assert.That(bytes[1], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawDataPreserved() {
    var rawData = new byte[17000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var file = new CfliDesignerFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = CfliDesignerWriter.ToBytes(file);

    for (var i = 0; i < rawData.Length; ++i)
      Assert.That(bytes[2 + i], Is.EqualTo(rawData[i]));
  }
}

[TestFixture]
public sealed class CfliDesignerRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[17000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new CfliDesignerFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = CfliDesignerWriter.ToBytes(original);
    var restored = CfliDesignerReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[17000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new CfliDesignerFile { LoadAddress = 0x3C00, RawData = rawData };
    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cfli");
    try {
      File.WriteAllBytes(tmpPath, CfliDesignerWriter.ToBytes(original));
      var restored = CfliDesignerReader.FromFile(new FileInfo(tmpPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerPayload_Preserved() {
    var rawData = new byte[20000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 3 % 256);

    var original = new CfliDesignerFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = CfliDesignerWriter.ToBytes(original);
    var restored = CfliDesignerReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class CfliDesignerDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsCfli() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".cfli"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsCfli() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".cfli"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CfliDesignerFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CfliDesignerFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => CfliDesignerFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(CfliDesignerFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(CfliDesignerFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesRgb24() {
    var data = TestHelpers._BuildValidCfliData(0x3C00);
    var file = CfliDesignerReader.FromBytes(data);
    var rawImage = CfliDesignerFile.ToRawImage(file);

    Assert.That(rawImage.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawImage.Width, Is.EqualTo(160));
    Assert.That(rawImage.Height, Is.EqualTo(200));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ScreenBankCount_Is8() {
    Assert.That(CfliDesignerFile.ScreenBankCount, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_IsBitmapPlus8ScreensPlusColor() {
    Assert.That(CfliDesignerFile.MinPayloadSize, Is.EqualTo(8000 + 8 * 1000 + 1000));
  }

  [Test]
  [Category("Unit")]
  public void DefaultRawData_IsEmpty() {
    var file = new CfliDesignerFile();
    Assert.That(file.RawData, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<CfliDesignerFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<CfliDesignerFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidCfliData(ushort loadAddress) {
    var rawData = new byte[17000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
