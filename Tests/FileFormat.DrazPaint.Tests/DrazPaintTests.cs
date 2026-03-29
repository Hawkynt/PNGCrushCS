using System;
using System.IO;
using FileFormat.DrazPaint;
using FileFormat.Core;

namespace FileFormat.DrazPaint.Tests;

[TestFixture]
public sealed class DrazPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".drz"));
    Assert.Throws<FileNotFoundException>(() => DrazPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => DrazPaintReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var file = TestHelpers._BuildValidDrazPaintFile(0x5800, 0x07);
    var bytes = DrazPaintWriter.ToBytes(file);
    var result = DrazPaintReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5800));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }
}

[TestFixture]
public sealed class DrazPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithLoadAddress() {
    var file = new DrazPaintFile {
      LoadAddress = 0x5800,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };
    var bytes = DrazPaintWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x58));
    Assert.That(bytes.Length, Is.GreaterThan(2));
  }
}

[TestFixture]
public sealed class DrazPaintRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var original = TestHelpers._BuildValidDrazPaintFile(0x5800, 14);

    var bytes = DrazPaintWriter.ToBytes(original);
    var restored = DrazPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }
}

[TestFixture]
public sealed class DrazPaintDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsDrz() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".drz"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsDrz() {
    Assert.That(_GetFileExtensions(), Does.Contain(".drz"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => DrazPaintFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<DrazPaintFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<DrazPaintFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static DrazPaintFile _BuildValidDrazPaintFile(ushort loadAddress, byte backgroundColor) {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
  }
}
