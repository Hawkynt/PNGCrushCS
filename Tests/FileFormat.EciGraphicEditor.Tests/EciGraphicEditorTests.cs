using System;
using System.IO;
using FileFormat.EciGraphicEditor;
using FileFormat.Core;

namespace FileFormat.EciGraphicEditor.Tests;

[TestFixture]
public sealed class EciGraphicEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".eci"));
    Assert.Throws<FileNotFoundException>(() => EciGraphicEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => EciGraphicEditorReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidEciData(0x3C00);
    var result = EciGraphicEditorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(EciGraphicEditorFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = TestHelpers._BuildValidEciData(0x5C00);
    var result = EciGraphicEditorReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawData_CopiedCorrectly() {
    var data = TestHelpers._BuildValidEciData(0x3C00);
    data[2] = 0xAB;
    data[100] = 0xCD;

    var result = EciGraphicEditorReader.FromBytes(data);

    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[98], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidEciData(0x5C00);

    using var ms = new MemoryStream(data);
    var result = EciGraphicEditorReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }
}

[TestFixture]
public sealed class EciGraphicEditorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[EciGraphicEditorFile.MinPayloadSize];
    var file = new EciGraphicEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = EciGraphicEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + EciGraphicEditorFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new EciGraphicEditorFile { LoadAddress = 0x5C00, RawData = new byte[EciGraphicEditorFile.MinPayloadSize] };
    var bytes = EciGraphicEditorWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x5C));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawData_WrittenAfterLoadAddress() {
    var rawData = new byte[EciGraphicEditorFile.MinPayloadSize];
    rawData[0] = 0xAA;
    rawData[EciGraphicEditorFile.MinPayloadSize - 1] = 0xBB;
    var file = new EciGraphicEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = EciGraphicEditorWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[2 + EciGraphicEditorFile.MinPayloadSize - 1], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LargerPayload_PreservedCompletely() {
    var rawData = new byte[32770];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var file = new EciGraphicEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = EciGraphicEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 32770));
  }
}

[TestFixture]
public sealed class EciGraphicEditorRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[EciGraphicEditorFile.MinPayloadSize + 770];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new EciGraphicEditorFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = EciGraphicEditorWriter.ToBytes(original);
    var restored = EciGraphicEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new EciGraphicEditorFile { LoadAddress = 0x4000, RawData = new byte[EciGraphicEditorFile.MinPayloadSize] };

    var bytes = EciGraphicEditorWriter.ToBytes(original);
    var restored = EciGraphicEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var rawData = new byte[EciGraphicEditorFile.MinPayloadSize];
    Array.Fill(rawData, (byte)0xFF);

    var original = new EciGraphicEditorFile { LoadAddress = 0xFFFF, RawData = rawData };

    var bytes = EciGraphicEditorWriter.ToBytes(original);
    var restored = EciGraphicEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new EciGraphicEditorFile { LoadAddress = 0x3C00, RawData = new byte[EciGraphicEditorFile.MinPayloadSize] };

    var bytes = EciGraphicEditorWriter.ToBytes(original);
    var restored = EciGraphicEditorReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(200));
  }
}

[TestFixture]
public sealed class EciGraphicEditorDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsEci() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".eci"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsEciAndEcp() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".eci"));
    Assert.That(extensions, Does.Contain(".ecp"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EciGraphicEditorFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => EciGraphicEditorFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidFile_ReturnsRgb24() {
    var rawData = new byte[EciGraphicEditorFile.MinPayloadSize];
    var file = new EciGraphicEditorFile { LoadAddress = 0x3C00, RawData = rawData };

    var image = EciGraphicEditorFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(160));
    Assert.That(image.Height, Is.EqualTo(200));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is160() {
    Assert.That(EciGraphicEditorFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is200() {
    Assert.That(EciGraphicEditorFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_Is18000() {
    Assert.That(EciGraphicEditorFile.MinPayloadSize, Is.EqualTo(8000 + 1000 + 8000 + 1000 + 1000));
  }

  private static string _GetPrimaryExtension() => _Helper<EciGraphicEditorFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<EciGraphicEditorFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidEciData(ushort loadAddress) {
    var rawData = new byte[EciGraphicEditorFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
