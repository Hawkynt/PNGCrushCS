using System;
using System.IO;
using FileFormat.PrintfoxPagefox;
using FileFormat.Core;

namespace FileFormat.PrintfoxPagefox.Tests;

[TestFixture]
public sealed class PrintfoxPagefoxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintfoxPagefoxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bs"));
    Assert.Throws<FileNotFoundException>(() => PrintfoxPagefoxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintfoxPagefoxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PrintfoxPagefoxReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[8000];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = PrintfoxPagefoxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.RawData.Length, Is.EqualTo(8000));
  }
}

[TestFixture]
public sealed class PrintfoxPagefoxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintfoxPagefoxWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var file = new PrintfoxPagefoxFile { RawData = new byte[8000] };
    var bytes = PrintfoxPagefoxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8000));
  }
}

[TestFixture]
public sealed class PrintfoxPagefoxRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[8000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 19 % 256);

    var original = new PrintfoxPagefoxFile { RawData = rawData };

    var bytes = PrintfoxPagefoxWriter.ToBytes(original);
    var restored = PrintfoxPagefoxReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class PrintfoxPagefoxDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsBs() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".bs"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsBsAndPg() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".bs"));
    Assert.That(extensions, Does.Contain(".pg"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintfoxPagefoxFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintfoxPagefoxFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Indexed1, PixelData = new byte[40 * 200] };
    Assert.Throws<NotSupportedException>(() => PrintfoxPagefoxFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed1Format() {
    var file = new PrintfoxPagefoxFile { RawData = new byte[8000] };
    var raw = PrintfoxPagefoxFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
  }

  private static string _GetPrimaryExtension() => _Helper<PrintfoxPagefoxFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<PrintfoxPagefoxFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}
