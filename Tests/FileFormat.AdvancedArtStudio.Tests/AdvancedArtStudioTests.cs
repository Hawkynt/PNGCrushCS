using System;
using System.IO;
using FileFormat.AdvancedArtStudio;
using FileFormat.Core;

namespace FileFormat.AdvancedArtStudio.Tests;

[TestFixture]
public sealed class AdvancedArtStudioReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AdvancedArtStudioReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ocp"));
    Assert.Throws<FileNotFoundException>(() => AdvancedArtStudioReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AdvancedArtStudioReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => AdvancedArtStudioReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidFile(0x2000, 0x03, 0x01);
    var result = AdvancedArtStudioReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
    Assert.That(result.BorderColor, Is.EqualTo(0x01));
  }
}

[TestFixture]
public sealed class AdvancedArtStudioWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AdvancedArtStudioWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var file = new AdvancedArtStudioFile {
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
      BorderColor = 0,
    };
    var bytes = AdvancedArtStudioWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(AdvancedArtStudioFile.ExpectedFileSize));
  }
}

[TestFixture]
public sealed class AdvancedArtStudioRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var original = new AdvancedArtStudioFile {
      LoadAddress = 0x2000,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = 11,
      BorderColor = 5,
    };

    var bytes = AdvancedArtStudioWriter.ToBytes(original);
    var restored = AdvancedArtStudioReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    Assert.That(restored.BorderColor, Is.EqualTo(original.BorderColor));
  }
}

[TestFixture]
public sealed class AdvancedArtStudioDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsOcp() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".ocp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsOcp() {
    Assert.That(_GetFileExtensions(), Does.Contain(".ocp"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AdvancedArtStudioFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AdvancedArtStudioFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => AdvancedArtStudioFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<AdvancedArtStudioFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<AdvancedArtStudioFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file class TestHelpers {
  internal static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor, byte borderColor) {
    var data = new byte[AdvancedArtStudioFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    data[10002] = backgroundColor;
    data[10003] = borderColor;

    return data;
  }
}
