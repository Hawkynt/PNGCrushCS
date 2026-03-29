using System;
using System.IO;
using FileFormat.HiresInterlaceFeniks;
using FileFormat.Core;

namespace FileFormat.HiresInterlaceFeniks.Tests;

[TestFixture]
public sealed class HiresInterlaceFeniksReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hlf"));
    Assert.Throws<FileNotFoundException>(() => HiresInterlaceFeniksReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => HiresInterlaceFeniksReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidHiresInterlaceFeniksData(0x3C00);
    var result = HiresInterlaceFeniksReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HiresInterlaceFeniksFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidHiresInterlaceFeniksData(0x4000);
    using var ms = new MemoryStream(data);
    var result = HiresInterlaceFeniksReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HiresInterlaceFeniksFile.MinPayloadSize));
  }
}

[TestFixture]
public sealed class HiresInterlaceFeniksWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize];
    var file = new HiresInterlaceFeniksFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = HiresInterlaceFeniksWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(HiresInterlaceFeniksFile.LoadAddressSize + HiresInterlaceFeniksFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_IsLittleEndian() {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize];
    var file = new HiresInterlaceFeniksFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = HiresInterlaceFeniksWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x3C));
  }
}

[TestFixture]
public sealed class HiresInterlaceFeniksRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize + 500];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new HiresInterlaceFeniksFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = HiresInterlaceFeniksWriter.ToBytes(original);
    var restored = HiresInterlaceFeniksReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new HiresInterlaceFeniksFile { LoadAddress = 0x4000, RawData = rawData };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hlf");
    try {
      File.WriteAllBytes(path, HiresInterlaceFeniksWriter.ToBytes(original));
      var restored = HiresInterlaceFeniksReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}

[TestFixture]
public sealed class HiresInterlaceFeniksDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHlf() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".hlf"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHlf() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".hlf"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => HiresInterlaceFeniksFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidData_ProducesRgb24() {
    var data = TestHelpers._BuildValidHiresInterlaceFeniksData(0x3C00);
    var file = HiresInterlaceFeniksReader.FromBytes(data);
    var raw = HiresInterlaceFeniksFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_InterlaceBlend_AveragesColors() {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize];

    // Frame 1: all bitmap bits set, screen byte = 0x10 => fg=1(white), bg=0(black)
    for (var i = 0; i < HiresInterlaceFeniksFile.BitmapDataSize; ++i)
      rawData[i] = 0xFF;
    for (var i = 0; i < HiresInterlaceFeniksFile.ScreenRamSize; ++i)
      rawData[HiresInterlaceFeniksFile.BitmapDataSize + i] = 0x10;

    // Frame 2: all bitmap bits clear => bg color = 0(black)
    var frame2Offset = HiresInterlaceFeniksFile.FrameSize;
    for (var i = 0; i < HiresInterlaceFeniksFile.BitmapDataSize; ++i)
      rawData[frame2Offset + i] = 0x00;
    for (var i = 0; i < HiresInterlaceFeniksFile.ScreenRamSize; ++i)
      rawData[frame2Offset + HiresInterlaceFeniksFile.BitmapDataSize + i] = 0x10;

    var fileData = new byte[HiresInterlaceFeniksFile.LoadAddressSize + rawData.Length];
    fileData[0] = 0x00;
    fileData[1] = 0x3C;
    Array.Copy(rawData, 0, fileData, HiresInterlaceFeniksFile.LoadAddressSize, rawData.Length);

    var file = HiresInterlaceFeniksReader.FromBytes(fileData);
    var raw = HiresInterlaceFeniksFile.ToRawImage(file);

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
    Assert.That(HiresInterlaceFeniksFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(HiresInterlaceFeniksFile.FixedHeight, Is.EqualTo(200));
  }

  private static string _GetPrimaryExtension() => _Helper<HiresInterlaceFeniksFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<HiresInterlaceFeniksFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidHiresInterlaceFeniksData(ushort loadAddress) {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[HiresInterlaceFeniksFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, HiresInterlaceFeniksFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
