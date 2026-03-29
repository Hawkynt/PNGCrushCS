using System;
using System.IO;
using FileFormat.HiresFliCrest;
using FileFormat.Core;

namespace FileFormat.HiresFliCrest.Tests;

[TestFixture]
public sealed class HiresFliCrestReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresFliCrestReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hfc"));
    Assert.Throws<FileNotFoundException>(() => HiresFliCrestReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresFliCrestReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => HiresFliCrestReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidHiresFliCrestData(0x3C00);
    var result = HiresFliCrestReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HiresFliCrestFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresFliCrestReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidHiresFliCrestData(0x4000);
    using var ms = new MemoryStream(data);
    var result = HiresFliCrestReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HiresFliCrestFile.MinPayloadSize));
  }
}

[TestFixture]
public sealed class HiresFliCrestWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresFliCrestWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[HiresFliCrestFile.MinPayloadSize];
    var file = new HiresFliCrestFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = HiresFliCrestWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(HiresFliCrestFile.LoadAddressSize + HiresFliCrestFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_IsLittleEndian() {
    var rawData = new byte[HiresFliCrestFile.MinPayloadSize];
    var file = new HiresFliCrestFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = HiresFliCrestWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x3C));
  }
}

[TestFixture]
public sealed class HiresFliCrestRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[HiresFliCrestFile.MinPayloadSize + 500];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new HiresFliCrestFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = HiresFliCrestWriter.ToBytes(original);
    var restored = HiresFliCrestReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[HiresFliCrestFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new HiresFliCrestFile { LoadAddress = 0x4000, RawData = rawData };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hfc");
    try {
      File.WriteAllBytes(path, HiresFliCrestWriter.ToBytes(original));
      var restored = HiresFliCrestReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}

[TestFixture]
public sealed class HiresFliCrestDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHfc() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".hfc"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHfc() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".hfc"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresFliCrestFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresFliCrestFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => HiresFliCrestFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidData_ProducesRgb24() {
    var data = TestHelpers._BuildValidHiresFliCrestData(0x3C00);
    var file = HiresFliCrestReader.FromBytes(data);
    var raw = HiresFliCrestFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is320() {
    Assert.That(HiresFliCrestFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(HiresFliCrestFile.FixedHeight, Is.EqualTo(200));
  }

  private static string _GetPrimaryExtension() => _Helper<HiresFliCrestFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<HiresFliCrestFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidHiresFliCrestData(ushort loadAddress) {
    var rawData = new byte[HiresFliCrestFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[HiresFliCrestFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, HiresFliCrestFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
