using System;
using System.IO;
using FileFormat.GephardHires;
using FileFormat.Core;

namespace FileFormat.GephardHires.Tests;

[TestFixture]
public sealed class GephardHiresReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GephardHiresReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ghg"));
    Assert.Throws<FileNotFoundException>(() => GephardHiresReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GephardHiresReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GephardHiresReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidGephardHiresData(0x2000);
    var result = GephardHiresReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(GephardHiresFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GephardHiresReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidGephardHiresData(0x4000);
    using var ms = new MemoryStream(data);
    var result = GephardHiresReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(GephardHiresFile.MinPayloadSize));
  }
}

[TestFixture]
public sealed class GephardHiresWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GephardHiresWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[GephardHiresFile.MinPayloadSize];
    var file = new GephardHiresFile { LoadAddress = 0x2000, RawData = rawData };
    var bytes = GephardHiresWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(GephardHiresFile.LoadAddressSize + GephardHiresFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_IsLittleEndian() {
    var rawData = new byte[GephardHiresFile.MinPayloadSize];
    var file = new GephardHiresFile { LoadAddress = 0x2000, RawData = rawData };
    var bytes = GephardHiresWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x20));
  }
}

[TestFixture]
public sealed class GephardHiresRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[GephardHiresFile.MinPayloadSize + 200];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new GephardHiresFile { LoadAddress = 0x2000, RawData = rawData };

    var bytes = GephardHiresWriter.ToBytes(original);
    var restored = GephardHiresReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[GephardHiresFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new GephardHiresFile { LoadAddress = 0x4000, RawData = rawData };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ghg");
    try {
      File.WriteAllBytes(path, GephardHiresWriter.ToBytes(original));
      var restored = GephardHiresReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}

[TestFixture]
public sealed class GephardHiresDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsGhg() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".ghg"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsGhg() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".ghg"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GephardHiresFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GephardHiresFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => GephardHiresFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidData_ProducesRgb24() {
    var data = TestHelpers._BuildValidGephardHiresData(0x2000);
    var file = GephardHiresReader.FromBytes(data);
    var raw = GephardHiresFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is320() {
    Assert.That(GephardHiresFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(GephardHiresFile.FixedHeight, Is.EqualTo(200));
  }

  private static string _GetPrimaryExtension() => _Helper<GephardHiresFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<GephardHiresFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidGephardHiresData(ushort loadAddress) {
    var rawData = new byte[GephardHiresFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[GephardHiresFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, GephardHiresFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
