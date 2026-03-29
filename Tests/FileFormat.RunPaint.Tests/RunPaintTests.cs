using System;
using System.IO;
using FileFormat.RunPaint;
using FileFormat.Core;

namespace FileFormat.RunPaint.Tests;

[TestFixture]
public sealed class RunPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RunPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rpm"));
    Assert.Throws<FileNotFoundException>(() => RunPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RunPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => RunPaintReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var file = TestHelpers._BuildValidRunPaintFile(0x6000, 0x05);
    var bytes = RunPaintWriter.ToBytes(file);
    var result = RunPaintReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }
}

[TestFixture]
public sealed class RunPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RunPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithLoadAddress() {
    var file = new RunPaintFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
    };
    var bytes = RunPaintWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x60));
    Assert.That(bytes.Length, Is.GreaterThan(2));
  }
}

[TestFixture]
public sealed class RunPaintRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var original = TestHelpers._BuildValidRunPaintFile(0x6000, 11);

    var bytes = RunPaintWriter.ToBytes(original);
    var restored = RunPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }
}

[TestFixture]
public sealed class RunPaintDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsRpm() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".rpm"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsRpm() {
    Assert.That(_GetFileExtensions(), Does.Contain(".rpm"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RunPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RunPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => RunPaintFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<RunPaintFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<RunPaintFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static RunPaintFile _BuildValidRunPaintFile(ushort loadAddress, byte backgroundColor) {
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
