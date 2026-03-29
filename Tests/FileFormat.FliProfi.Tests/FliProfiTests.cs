using System;
using System.IO;
using FileFormat.FliProfi;
using FileFormat.Core;

namespace FileFormat.FliProfi.Tests;

[TestFixture]
public sealed class FliProfiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr"));
    Assert.Throws<FileNotFoundException>(() => FliProfiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FliProfiReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidFliProfiData(0x3C00);
    var result = FliProfiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesLoadAddress() {
    var data = TestHelpers._BuildValidFliProfiData(0x4000);
    var result = FliProfiReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_RawDataLength() {
    var data = TestHelpers._BuildValidFliProfiData(0x3C00);
    var result = FliProfiReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(data.Length - FliProfiFile.LoadAddressSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_RawDataPreserved() {
    var data = TestHelpers._BuildValidFliProfiData(0x3C00);
    var result = FliProfiReader.FromBytes(data);

    for (var i = 0; i < result.RawData.Length; ++i)
      Assert.That(result.RawData[i], Is.EqualTo(data[i + FliProfiFile.LoadAddressSize]), $"RawData mismatch at index {i}");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinimumSize_Accepted() {
    var minSize = FliProfiFile.LoadAddressSize + FliProfiFile.MinPayloadSize;
    var data = new byte[minSize];
    data[0] = 0x00;
    data[1] = 0x3C;

    var result = FliProfiReader.FromBytes(data);
    Assert.That(result.RawData.Length, Is.EqualTo(FliProfiFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_JustBelowMinimumSize_ThrowsInvalidDataException() {
    var tooSmall = FliProfiFile.LoadAddressSize + FliProfiFile.MinPayloadSize - 1;
    Assert.Throws<InvalidDataException>(() => FliProfiReader.FromBytes(new byte[tooSmall]));
  }
}

[TestFixture]
public sealed class FliProfiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    var file = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = FliProfiWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(FliProfiFile.LoadAddressSize + rawData.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddressLittleEndian() {
    var file = new FliProfiFile { LoadAddress = 0x4000, RawData = new byte[FliProfiFile.MinPayloadSize] };
    var bytes = FliProfiWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawDataPreserved() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var file = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = FliProfiWriter.ToBytes(file);

    for (var i = 0; i < rawData.Length; ++i)
      Assert.That(bytes[FliProfiFile.LoadAddressSize + i], Is.EqualTo(rawData[i]), $"Data mismatch at offset {i}");
  }
}

[TestFixture]
public sealed class FliProfiRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[FliProfiFile.MinPayloadSize + 500];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = FliProfiWriter.ToBytes(original);
    var restored = FliProfiReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new FliProfiFile { LoadAddress = 0x4000, RawData = rawData };

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr");
    try {
      File.WriteAllBytes(tmp, FliProfiWriter.ToBytes(original));
      var restored = FliProfiReader.FromFile(new FileInfo(tmp));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream_PreservesData() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = FliProfiWriter.ToBytes(original);

    using var ms = new MemoryStream(bytes);
    var restored = FliProfiReader.FromStream(ms);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    var original = new FliProfiFile { LoadAddress = 0x0000, RawData = rawData };

    var bytes = FliProfiWriter.ToBytes(original);
    var restored = FliProfiReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0));
    Assert.That(restored.RawData, Is.EqualTo(rawData));
  }
}

[TestFixture]
public sealed class FliProfiDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsFpr() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".fpr"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsFpr() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".fpr"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => FliProfiFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_CorrectDimensions() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    var file = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };
    var image = FliProfiFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(160));
    Assert.That(image.Height, Is.EqualTo(200));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_CorrectPixelDataSize() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    var file = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };
    var image = FliProfiFile.ToRawImage(file);

    Assert.That(image.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AllZeroBitmap_AllBlack() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    var file = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };
    var image = FliProfiFile.ToRawImage(file);

    // All zero bitmap means pixelValue=0 which maps to background color (index 0 = black)
    for (var i = 0; i < image.PixelData.Length; ++i)
      Assert.That(image.PixelData[i], Is.EqualTo(0), $"Pixel data byte {i} should be 0 (black)");
  }

  [Test]
  [Category("Unit")]
  public void Width_Is160() {
    var file = new FliProfiFile { RawData = new byte[FliProfiFile.MinPayloadSize] };
    Assert.That(file.Width, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void Height_Is200() {
    var file = new FliProfiFile { RawData = new byte[FliProfiFile.MinPayloadSize] };
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void Constants_BitmapSize_Is8000() {
    Assert.That(FliProfiFile.BitmapSize, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void Constants_ScreenBankCount_Is8() {
    Assert.That(FliProfiFile.ScreenBankCount, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void Constants_ColorRamSize_Is1000() {
    Assert.That(FliProfiFile.ColorRamSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void Constants_MinPayloadSize_IsCorrect() {
    Assert.That(FliProfiFile.MinPayloadSize, Is.EqualTo(8000 + 8 * 1000 + 1000));
  }

  [Test]
  [Category("Unit")]
  public void Defaults_LoadAddressIsZero() {
    var file = new FliProfiFile();
    Assert.That(file.LoadAddress, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Defaults_RawDataEmpty() {
    var file = new FliProfiFile();
    Assert.That(file.RawData, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<FliProfiFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<FliProfiFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidFliProfiData(ushort loadAddress) {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[FliProfiFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, FliProfiFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
