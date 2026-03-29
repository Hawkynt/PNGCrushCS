using System;
using System.IO;
using FileFormat.LogoPainter;
using FileFormat.Core;

namespace FileFormat.LogoPainter.Tests;

[TestFixture]
public sealed class LogoPainterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoPainterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lp3"));
    Assert.Throws<FileNotFoundException>(() => LogoPainterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoPainterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => LogoPainterReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidLogoPainterData(0x4000);
    var result = LogoPainterReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(10000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoPainterReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidLogoPainterData(0x4000);
    using var ms = new MemoryStream(data);
    var result = LogoPainterReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.EqualTo(10000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_LittleEndian() {
    var data = TestHelpers._BuildValidLogoPainterData(0xABCD);
    var result = LogoPainterReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0xABCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawData_IsCopy() {
    var data = TestHelpers._BuildValidLogoPainterData(0x4000);
    var result = LogoPainterReader.FromBytes(data);
    var originalFirst = result.RawData[0];

    data[2] = (byte)(originalFirst ^ 0xFF);
    Assert.That(result.RawData[0], Is.EqualTo(originalFirst));
  }
}

[TestFixture]
public sealed class LogoPainterWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoPainterWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[10000];
    var file = new LogoPainterFile { LoadAddress = 0x4000, RawData = rawData };
    var bytes = LogoPainterWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 10000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_LittleEndian() {
    var file = new LogoPainterFile { LoadAddress = 0xABCD, RawData = new byte[10000] };
    var bytes = LogoPainterWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xCD));
    Assert.That(bytes[1], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawDataPreserved() {
    var rawData = new byte[10000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var file = new LogoPainterFile { LoadAddress = 0x4000, RawData = rawData };
    var bytes = LogoPainterWriter.ToBytes(file);

    for (var i = 0; i < rawData.Length; ++i)
      Assert.That(bytes[2 + i], Is.EqualTo(rawData[i]));
  }
}

[TestFixture]
public sealed class LogoPainterRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[10000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new LogoPainterFile { LoadAddress = 0x4000, RawData = rawData };

    var bytes = LogoPainterWriter.ToBytes(original);
    var restored = LogoPainterReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[10000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new LogoPainterFile { LoadAddress = 0x4000, RawData = rawData };
    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lp3");
    try {
      File.WriteAllBytes(tmpPath, LogoPainterWriter.ToBytes(original));
      var restored = LogoPainterReader.FromFile(new FileInfo(tmpPath));

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
    var rawData = new byte[12000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 3 % 256);

    var original = new LogoPainterFile { LoadAddress = 0x4000, RawData = rawData };

    var bytes = LogoPainterWriter.ToBytes(original);
    var restored = LogoPainterReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class LogoPainterDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsLp3() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".lp3"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsLp3() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".lp3"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoPainterFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoPainterFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => LogoPainterFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(LogoPainterFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(LogoPainterFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesRgb24() {
    var data = TestHelpers._BuildValidLogoPainterData(0x4000);
    var file = LogoPainterReader.FromBytes(data);
    var rawImage = LogoPainterFile.ToRawImage(file);

    Assert.That(rawImage.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawImage.Width, Is.EqualTo(160));
    Assert.That(rawImage.Height, Is.EqualTo(200));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void DefaultRawData_IsEmpty() {
    var file = new LogoPainterFile();
    Assert.That(file.RawData, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<LogoPainterFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<LogoPainterFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidLogoPainterData(ushort loadAddress) {
    var rawData = new byte[10000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
