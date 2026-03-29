using System;
using System.IO;
using FileFormat.MuifliEditor;
using FileFormat.Core;

namespace FileFormat.MuifliEditor.Tests;

[TestFixture]
public sealed class MuifliEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MuifliEditorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".muf"));
    Assert.Throws<FileNotFoundException>(() => MuifliEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MuifliEditorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => MuifliEditorReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidMuifliEditorData(0x3C00);
    var result = MuifliEditorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(34384));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MuifliEditorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidMuifliEditorData(0x3C00);
    using var ms = new MemoryStream(data);
    var result = MuifliEditorReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.EqualTo(34384));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_LittleEndian() {
    var data = TestHelpers._BuildValidMuifliEditorData(0xABCD);
    var result = MuifliEditorReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0xABCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactMinSize_Succeeds() {
    var data = TestHelpers._BuildValidMuifliEditorData(0x3C00);
    Assert.That(data.Length, Is.EqualTo(2 + 34384));
    Assert.DoesNotThrow(() => MuifliEditorReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OneBelowMinSize_ThrowsInvalidDataException() {
    var data = new byte[2 + 34384 - 1];
    Assert.Throws<InvalidDataException>(() => MuifliEditorReader.FromBytes(data));
  }
}

[TestFixture]
public sealed class MuifliEditorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MuifliEditorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[34384];
    var file = new MuifliEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = MuifliEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 34384));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_LittleEndian() {
    var file = new MuifliEditorFile { LoadAddress = 0xABCD, RawData = new byte[34384] };
    var bytes = MuifliEditorWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xCD));
    Assert.That(bytes[1], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawDataPreserved() {
    var rawData = new byte[34384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var file = new MuifliEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = MuifliEditorWriter.ToBytes(file);

    for (var i = 0; i < rawData.Length; ++i)
      Assert.That(bytes[2 + i], Is.EqualTo(rawData[i]));
  }
}

[TestFixture]
public sealed class MuifliEditorRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[34384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new MuifliEditorFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = MuifliEditorWriter.ToBytes(original);
    var restored = MuifliEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[34384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new MuifliEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".muf");
    try {
      File.WriteAllBytes(tmpPath, MuifliEditorWriter.ToBytes(original));
      var restored = MuifliEditorReader.FromFile(new FileInfo(tmpPath));

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
    var rawData = new byte[40000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 3 % 256);

    var original = new MuifliEditorFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = MuifliEditorWriter.ToBytes(original);
    var restored = MuifliEditorReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class MuifliEditorDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsMuf() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".muf"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsMufMuiMup() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".muf"));
    Assert.That(extensions, Does.Contain(".mui"));
    Assert.That(extensions, Does.Contain(".mup"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MuifliEditorFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MuifliEditorFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => MuifliEditorFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(MuifliEditorFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(MuifliEditorFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesRgb24() {
    var data = TestHelpers._BuildValidMuifliEditorData(0x3C00);
    var file = MuifliEditorReader.FromBytes(data);
    var rawImage = MuifliEditorFile.ToRawImage(file);

    Assert.That(rawImage.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawImage.Width, Is.EqualTo(160));
    Assert.That(rawImage.Height, Is.EqualTo(200));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FrameCount_Is2() {
    Assert.That(MuifliEditorFile.FrameCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ScreenBankCount_Is8() {
    Assert.That(MuifliEditorFile.ScreenBankCount, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ScreenBankSize_Is1024() {
    Assert.That(MuifliEditorFile.ScreenBankSize, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void FrameSize_IsBitmapPlus8BanksPlusColor() {
    Assert.That(MuifliEditorFile.FrameSize, Is.EqualTo(8000 + 8 * 1024 + 1000));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_IsTwoFrames() {
    Assert.That(MuifliEditorFile.MinPayloadSize, Is.EqualTo(2 * (8000 + 8 * 1024 + 1000)));
  }

  [Test]
  [Category("Unit")]
  public void DefaultRawData_IsEmpty() {
    var file = new MuifliEditorFile();
    Assert.That(file.RawData, Is.Empty);
  }

  private static string _GetPrimaryExtension() => _Helper<MuifliEditorFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<MuifliEditorFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidMuifliEditorData(ushort loadAddress) {
    var rawData = new byte[34384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
