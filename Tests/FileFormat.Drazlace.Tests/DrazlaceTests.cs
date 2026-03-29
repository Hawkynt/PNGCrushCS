using System;
using System.IO;
using FileFormat.Drazlace;
using FileFormat.Core;

namespace FileFormat.Drazlace.Tests;

[TestFixture]
public sealed class DrazlaceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazlaceReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dlp"));
    Assert.Throws<FileNotFoundException>(() => DrazlaceReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazlaceReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => DrazlaceReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var file = TestHelpers._BuildValidDrazlaceFile(0x5800, 0x07);
    var bytes = DrazlaceWriter.ToBytes(file);
    var result = DrazlaceReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5800));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }
}

[TestFixture]
public sealed class DrazlaceWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazlaceWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithLoadAddress() {
    var file = new DrazlaceFile {
      LoadAddress = 0x5800,
      BitmapData1 = new byte[8000],
      ScreenRam1 = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
      BitmapData2 = new byte[8000],
      ScreenRam2 = new byte[1000],
    };
    var bytes = DrazlaceWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x58));
    Assert.That(bytes.Length, Is.GreaterThan(2));
  }
}

[TestFixture]
public sealed class DrazlaceRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var original = TestHelpers._BuildValidDrazlaceFile(0x5800, 14);

    var bytes = DrazlaceWriter.ToBytes(original);
    var restored = DrazlaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData1, Is.EqualTo(original.BitmapData1));
    Assert.That(restored.ScreenRam1, Is.EqualTo(original.ScreenRam1));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    Assert.That(restored.BitmapData2, Is.EqualTo(original.BitmapData2));
    Assert.That(restored.ScreenRam2, Is.EqualTo(original.ScreenRam2));
  }
}

[TestFixture]
public sealed class DrazlaceDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsDlp() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".dlp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsDlp() {
    Assert.That(_GetFileExtensions(), Does.Contain(".dlp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsDrl() {
    Assert.That(_GetFileExtensions(), Does.Contain(".drl"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazlaceFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazlaceFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => DrazlaceFile.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = TestHelpers._BuildValidDrazlaceFile(0x5800, 0x00);
    var raw = DrazlaceFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  private static string _GetPrimaryExtension() => _Helper<DrazlaceFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<DrazlaceFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static DrazlaceFile _BuildValidDrazlaceFile(ushort loadAddress, byte backgroundColor) {
    var bitmapData1 = new byte[8000];
    for (var i = 0; i < bitmapData1.Length; ++i)
      bitmapData1[i] = (byte)(i * 7 % 256);

    var screenRam1 = new byte[1000];
    for (var i = 0; i < screenRam1.Length; ++i)
      screenRam1[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var bitmapData2 = new byte[8000];
    for (var i = 0; i < bitmapData2.Length; ++i)
      bitmapData2[i] = (byte)(i * 11 % 256);

    var screenRam2 = new byte[1000];
    for (var i = 0; i < screenRam2.Length; ++i)
      screenRam2[i] = (byte)((i * 5 + 2) % 16);

    return new() {
      LoadAddress = loadAddress,
      BitmapData1 = bitmapData1,
      ScreenRam1 = screenRam1,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BitmapData2 = bitmapData2,
      ScreenRam2 = screenRam2,
    };
  }
}
