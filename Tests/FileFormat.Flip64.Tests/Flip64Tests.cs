using System;
using System.IO;
using FileFormat.Flip64;
using FileFormat.Core;

namespace FileFormat.Flip64.Tests;

[TestFixture]
public sealed class Flip64ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Flip64Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fbi"));
    Assert.Throws<FileNotFoundException>(() => Flip64Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Flip64Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Flip64Reader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidFlip64Data(0x4000);
    var result = Flip64Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(19000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Flip64Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidFlip64Data(0x4000);
    using var ms = new MemoryStream(data);
    var result = Flip64Reader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.EqualTo(19000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_LittleEndian() {
    var data = TestHelpers._BuildValidFlip64Data(0xABCD);
    var result = Flip64Reader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0xABCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactMinSize_Succeeds() {
    var data = TestHelpers._BuildValidFlip64Data(0x4000);
    Assert.That(data.Length, Is.EqualTo(2 + 19000));
    Assert.DoesNotThrow(() => Flip64Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OneBelowMinSize_ThrowsInvalidDataException() {
    var data = new byte[2 + 19000 - 1];
    Assert.Throws<InvalidDataException>(() => Flip64Reader.FromBytes(data));
  }
}

[TestFixture]
public sealed class Flip64WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Flip64Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[19000];
    var file = new Flip64File { LoadAddress = 0x4000, RawData = rawData };
    var bytes = Flip64Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 19000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_LittleEndian() {
    var file = new Flip64File { LoadAddress = 0xABCD, RawData = new byte[19000] };
    var bytes = Flip64Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xCD));
    Assert.That(bytes[1], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawDataPreserved() {
    var rawData = new byte[19000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var file = new Flip64File { LoadAddress = 0x4000, RawData = rawData };
    var bytes = Flip64Writer.ToBytes(file);

    for (var i = 0; i < rawData.Length; ++i)
      Assert.That(bytes[2 + i], Is.EqualTo(rawData[i]));
  }
}

[TestFixture]
public sealed class Flip64RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[19000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new Flip64File { LoadAddress = 0x4000, RawData = rawData };

    var bytes = Flip64Writer.ToBytes(original);
    var restored = Flip64Reader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[19000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new Flip64File { LoadAddress = 0x4000, RawData = rawData };
    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fbi");
    try {
      File.WriteAllBytes(tmpPath, Flip64Writer.ToBytes(original));
      var restored = Flip64Reader.FromFile(new FileInfo(tmpPath));

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
    var rawData = new byte[22000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 3 % 256);

    var original = new Flip64File { LoadAddress = 0x4000, RawData = rawData };

    var bytes = Flip64Writer.ToBytes(original);
    var restored = Flip64Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class Flip64DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsFbi() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".fbi"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsFbi() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".fbi"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Flip64File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Flip64File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => Flip64File.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(Flip64File.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(Flip64File.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesRgb24() {
    var data = TestHelpers._BuildValidFlip64Data(0x4000);
    var file = Flip64Reader.FromBytes(data);
    var rawImage = Flip64File.ToRawImage(file);

    Assert.That(rawImage.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawImage.Width, Is.EqualTo(160));
    Assert.That(rawImage.Height, Is.EqualTo(200));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_IsTwoBitmapsTwoScreensPlusColor() {
    Assert.That(Flip64File.MinPayloadSize, Is.EqualTo(8000 + 1000 + 8000 + 1000 + 1000));
  }

  [Test]
  [Category("Unit")]
  public void DefaultRawData_IsEmpty() {
    var file = new Flip64File();
    Assert.That(file.RawData, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<Flip64File>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<Flip64File>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidFlip64Data(ushort loadAddress) {
    var rawData = new byte[19000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
