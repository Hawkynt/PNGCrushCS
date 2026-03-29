using System;
using System.IO;
using FileFormat.HardInterlace;
using FileFormat.Core;

namespace FileFormat.HardInterlace.Tests;

[TestFixture]
public sealed class HardInterlaceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HardInterlaceReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hip"));
    Assert.Throws<FileNotFoundException>(() => HardInterlaceReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HardInterlaceReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => HardInterlaceReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidHardInterlaceData(0x3C00);
    var result = HardInterlaceReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HardInterlaceFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HardInterlaceReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidHardInterlaceData(0x4000);
    using var ms = new MemoryStream(data);
    var result = HardInterlaceReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HardInterlaceFile.MinPayloadSize));
  }
}

[TestFixture]
public sealed class HardInterlaceWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HardInterlaceWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[HardInterlaceFile.MinPayloadSize];
    var file = new HardInterlaceFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = HardInterlaceWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(HardInterlaceFile.LoadAddressSize + HardInterlaceFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_IsLittleEndian() {
    var rawData = new byte[HardInterlaceFile.MinPayloadSize];
    var file = new HardInterlaceFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = HardInterlaceWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x3C));
  }
}

[TestFixture]
public sealed class HardInterlaceRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[HardInterlaceFile.MinPayloadSize + 500];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new HardInterlaceFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = HardInterlaceWriter.ToBytes(original);
    var restored = HardInterlaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[HardInterlaceFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new HardInterlaceFile { LoadAddress = 0x4000, RawData = rawData };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hip");
    try {
      File.WriteAllBytes(path, HardInterlaceWriter.ToBytes(original));
      var restored = HardInterlaceReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}

[TestFixture]
public sealed class HardInterlaceDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHlf() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".hip"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHlf() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".hip"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HardInterlaceFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HardInterlaceFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => HardInterlaceFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidData_ProducesRgb24() {
    var data = TestHelpers._BuildValidHardInterlaceData(0x3C00);
    var file = HardInterlaceReader.FromBytes(data);
    var raw = HardInterlaceFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_InterlaceBlend_AveragesColors() {
    var rawData = new byte[HardInterlaceFile.MinPayloadSize];

    // Frame 1: all bitmap bits set, screen byte = 0x10 => fg=1(white), bg=0(black)
    for (var i = 0; i < HardInterlaceFile.BitmapDataSize; ++i)
      rawData[i] = 0xFF;
    for (var i = 0; i < HardInterlaceFile.ScreenRamSize; ++i)
      rawData[HardInterlaceFile.BitmapDataSize + i] = 0x10;

    // Frame 2: all bitmap bits clear => bg color = 0(black)
    var frame2Offset = HardInterlaceFile.FrameSize;
    for (var i = 0; i < HardInterlaceFile.BitmapDataSize; ++i)
      rawData[frame2Offset + i] = 0x00;
    for (var i = 0; i < HardInterlaceFile.ScreenRamSize; ++i)
      rawData[frame2Offset + HardInterlaceFile.BitmapDataSize + i] = 0x10;

    var fileData = new byte[HardInterlaceFile.LoadAddressSize + rawData.Length];
    fileData[0] = 0x00;
    fileData[1] = 0x3C;
    Array.Copy(rawData, 0, fileData, HardInterlaceFile.LoadAddressSize, rawData.Length);

    var file = HardInterlaceReader.FromBytes(fileData);
    var raw = HardInterlaceFile.ToRawImage(file);

    // Frame 1 pixel (0,0) = white (0xFF,0xFF,0xFF), Frame 2 pixel (0,0) = black (0x00,0x00,0x00)
    // Average = (0x7F, 0x7F, 0x7F) or (0x80, 0x80, 0x80) depending on rounding
    var r = raw.PixelData[0];
    var g = raw.PixelData[1];
    var b = raw.PixelData[2];
    Assert.That(r, Is.InRange(0x7F, 0x80));
    Assert.That(g, Is.InRange(0x7F, 0x80));
    Assert.That(b, Is.InRange(0x7F, 0x80));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is320() {
    Assert.That(HardInterlaceFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(HardInterlaceFile.FixedHeight, Is.EqualTo(200));
  }

  private static string _GetPrimaryExtension() => _Helper<HardInterlaceFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<HardInterlaceFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidHardInterlaceData(ushort loadAddress) {
    var rawData = new byte[HardInterlaceFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[HardInterlaceFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, HardInterlaceFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
