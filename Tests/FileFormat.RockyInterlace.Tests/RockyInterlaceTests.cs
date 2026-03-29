using System;
using System.IO;
using FileFormat.RockyInterlace;
using FileFormat.Core;

namespace FileFormat.RockyInterlace.Tests;

[TestFixture]
public sealed class RockyInterlaceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RockyInterlaceReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rip"));
    Assert.Throws<FileNotFoundException>(() => RockyInterlaceReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RockyInterlaceReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => RockyInterlaceReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidRockyInterlaceData(0x3C00);
    var result = RockyInterlaceReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(RockyInterlaceFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RockyInterlaceReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidRockyInterlaceData(0x4000);
    using var ms = new MemoryStream(data);
    var result = RockyInterlaceReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(RockyInterlaceFile.MinPayloadSize));
  }
}

[TestFixture]
public sealed class RockyInterlaceWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RockyInterlaceWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[RockyInterlaceFile.MinPayloadSize];
    var file = new RockyInterlaceFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = RockyInterlaceWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(RockyInterlaceFile.LoadAddressSize + RockyInterlaceFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_IsLittleEndian() {
    var rawData = new byte[RockyInterlaceFile.MinPayloadSize];
    var file = new RockyInterlaceFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = RockyInterlaceWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x3C));
  }
}

[TestFixture]
public sealed class RockyInterlaceRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[RockyInterlaceFile.MinPayloadSize + 500];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new RockyInterlaceFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = RockyInterlaceWriter.ToBytes(original);
    var restored = RockyInterlaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[RockyInterlaceFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new RockyInterlaceFile { LoadAddress = 0x4000, RawData = rawData };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rip");
    try {
      File.WriteAllBytes(path, RockyInterlaceWriter.ToBytes(original));
      var restored = RockyInterlaceReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}

[TestFixture]
public sealed class RockyInterlaceDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHlf() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".rip"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHlf() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".rip"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RockyInterlaceFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RockyInterlaceFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => RockyInterlaceFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidData_ProducesRgb24() {
    var data = TestHelpers._BuildValidRockyInterlaceData(0x3C00);
    var file = RockyInterlaceReader.FromBytes(data);
    var raw = RockyInterlaceFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_InterlaceBlend_AveragesColors() {
    var rawData = new byte[RockyInterlaceFile.MinPayloadSize];

    // Frame 1: all bitmap bits set, screen byte = 0x10 => fg=1(white), bg=0(black)
    for (var i = 0; i < RockyInterlaceFile.BitmapDataSize; ++i)
      rawData[i] = 0xFF;
    for (var i = 0; i < RockyInterlaceFile.ScreenRamSize; ++i)
      rawData[RockyInterlaceFile.BitmapDataSize + i] = 0x10;

    // Frame 2: all bitmap bits clear => bg color = 0(black)
    var frame2Offset = RockyInterlaceFile.FrameSize;
    for (var i = 0; i < RockyInterlaceFile.BitmapDataSize; ++i)
      rawData[frame2Offset + i] = 0x00;
    for (var i = 0; i < RockyInterlaceFile.ScreenRamSize; ++i)
      rawData[frame2Offset + RockyInterlaceFile.BitmapDataSize + i] = 0x10;

    var fileData = new byte[RockyInterlaceFile.LoadAddressSize + rawData.Length];
    fileData[0] = 0x00;
    fileData[1] = 0x3C;
    Array.Copy(rawData, 0, fileData, RockyInterlaceFile.LoadAddressSize, rawData.Length);

    var file = RockyInterlaceReader.FromBytes(fileData);
    var raw = RockyInterlaceFile.ToRawImage(file);

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
    Assert.That(RockyInterlaceFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(RockyInterlaceFile.FixedHeight, Is.EqualTo(200));
  }

  private static string _GetPrimaryExtension() => _Helper<RockyInterlaceFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<RockyInterlaceFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidRockyInterlaceData(ushort loadAddress) {
    var rawData = new byte[RockyInterlaceFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[RockyInterlaceFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, RockyInterlaceFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
