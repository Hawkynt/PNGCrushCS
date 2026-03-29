using System;
using System.IO;
using FileFormat.NufliEditor;
using FileFormat.Core;

namespace FileFormat.NufliEditor.Tests;

[TestFixture]
public sealed class NufliEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NufliEditorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nuf"));
    Assert.Throws<FileNotFoundException>(() => NufliEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NufliEditorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => NufliEditorReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidNufliData(0x3C00);
    var result = NufliEditorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(19240));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NufliEditorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidNufliData(0x3C00);
    using var ms = new MemoryStream(data);
    var result = NufliEditorReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.EqualTo(19240));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_LittleEndian() {
    var data = TestHelpers._BuildValidNufliData(0xABCD);
    var result = NufliEditorReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0xABCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactMinSize_Succeeds() {
    var data = TestHelpers._BuildValidNufliData(0x3C00);
    Assert.That(data.Length, Is.EqualTo(2 + 19240));
    Assert.DoesNotThrow(() => NufliEditorReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OneBelowMinSize_ThrowsInvalidDataException() {
    var data = new byte[2 + 19240 - 1];
    Assert.Throws<InvalidDataException>(() => NufliEditorReader.FromBytes(data));
  }
}

[TestFixture]
public sealed class NufliEditorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NufliEditorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[19240];
    var file = new NufliEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = NufliEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 19240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_LittleEndian() {
    var file = new NufliEditorFile { LoadAddress = 0xABCD, RawData = new byte[19240] };
    var bytes = NufliEditorWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xCD));
    Assert.That(bytes[1], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawDataPreserved() {
    var rawData = new byte[19240];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var file = new NufliEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = NufliEditorWriter.ToBytes(file);

    for (var i = 0; i < rawData.Length; ++i)
      Assert.That(bytes[2 + i], Is.EqualTo(rawData[i]));
  }
}

[TestFixture]
public sealed class NufliEditorRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[19240];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new NufliEditorFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = NufliEditorWriter.ToBytes(original);
    var restored = NufliEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[19240];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new NufliEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nuf");
    try {
      File.WriteAllBytes(tmpPath, NufliEditorWriter.ToBytes(original));
      var restored = NufliEditorReader.FromFile(new FileInfo(tmpPath));

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

    var original = new NufliEditorFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = NufliEditorWriter.ToBytes(original);
    var restored = NufliEditorReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class NufliEditorDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsNuf() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".nuf"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsNuf() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".nuf"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsNup() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".nup"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NufliEditorFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NufliEditorFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => NufliEditorFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(NufliEditorFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(NufliEditorFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesRgb24() {
    var data = TestHelpers._BuildValidNufliData(0x3C00);
    var file = NufliEditorReader.FromBytes(data);
    var rawImage = NufliEditorFile.ToRawImage(file);

    Assert.That(rawImage.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawImage.Width, Is.EqualTo(160));
    Assert.That(rawImage.Height, Is.EqualTo(200));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ScreenBankCount_Is8() {
    Assert.That(NufliEditorFile.ScreenBankCount, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ScreenBankSize_Is1024() {
    Assert.That(NufliEditorFile.ScreenBankSize, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void SpriteDataSize_Is2048() {
    Assert.That(NufliEditorFile.SpriteDataSize, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_IsBitmapPlus8ScreensPlusColorPlusSprites() {
    Assert.That(NufliEditorFile.MinPayloadSize, Is.EqualTo(8000 + 8 * 1024 + 1000 + 2048));
  }

  [Test]
  [Category("Unit")]
  public void DefaultRawData_IsEmpty() {
    var file = new NufliEditorFile();
    Assert.That(file.RawData, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<NufliEditorFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<NufliEditorFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidNufliData(ushort loadAddress) {
    var rawData = new byte[19240];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
