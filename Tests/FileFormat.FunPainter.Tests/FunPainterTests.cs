using System;
using System.IO;
using FileFormat.FunPainter;
using FileFormat.Core;

namespace FileFormat.FunPainter.Tests;

[TestFixture]
public sealed class FunPainterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunPainterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fp2"));
    Assert.Throws<FileNotFoundException>(() => FunPainterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunPainterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FunPainterReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidFunPainterData(0x3C00);
    var result = FunPainterReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(8000));
  }
}

[TestFixture]
public sealed class FunPainterWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunPainterWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[33000];
    var file = new FunPainterFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = FunPainterWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 33000));
  }
}

[TestFixture]
public sealed class FunPainterRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[33000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new FunPainterFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = FunPainterWriter.ToBytes(original);
    var restored = FunPainterReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class FunPainterDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsFp2() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".fp2"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsFp2AndFun() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".fp2"));
    Assert.That(extensions, Does.Contain(".fun"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunPainterFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunPainterFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => FunPainterFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<FunPainterFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<FunPainterFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidFunPainterData(ushort loadAddress) {
    var rawData = new byte[10000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
