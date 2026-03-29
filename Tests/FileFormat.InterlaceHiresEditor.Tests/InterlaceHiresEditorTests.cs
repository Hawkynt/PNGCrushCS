using System;
using System.IO;
using FileFormat.InterlaceHiresEditor;
using FileFormat.Core;

namespace FileFormat.InterlaceHiresEditor.Tests;

[TestFixture]
public sealed class InterlaceHiresEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceHiresEditorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ihe"));
    Assert.Throws<FileNotFoundException>(() => InterlaceHiresEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceHiresEditorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => InterlaceHiresEditorReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidIheData(0x3C00);
    var result = InterlaceHiresEditorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(18000));
  }
}

[TestFixture]
public sealed class InterlaceHiresEditorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceHiresEditorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[18000];
    var file = new InterlaceHiresEditorFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = InterlaceHiresEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 18000));
  }
}

[TestFixture]
public sealed class InterlaceHiresEditorRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[18000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new InterlaceHiresEditorFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = InterlaceHiresEditorWriter.ToBytes(original);
    var restored = InterlaceHiresEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class InterlaceHiresEditorDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsIhe() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".ihe"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsIhe() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".ihe"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceHiresEditorFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceHiresEditorFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => InterlaceHiresEditorFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<InterlaceHiresEditorFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<InterlaceHiresEditorFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidIheData(ushort loadAddress) {
    var rawData = new byte[18000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
