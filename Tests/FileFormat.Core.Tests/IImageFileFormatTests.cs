using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Msp;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class IImageFileFormatTests {

  private static MspFile _CreateSimpleMspFile(int width = 16, int height = 8) {
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    return new() {
      Width = width,
      Height = height,
      Version = MspVersion.V2,
      PixelData = pixelData,
    };
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_ReturnsDotMsp() {
    var extension = _GetPrimaryExtension<MspFile>();
    Assert.That(extension, Is.EqualTo(".msp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsPrimaryExtension() {
    var extensions = _GetFileExtensions<MspFile>();
    Assert.That(extensions, Does.Contain(".msp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_IsNotEmpty() {
    var extensions = _GetFileExtensions<MspFile>();
    Assert.That(extensions, Is.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesValidRawImage() {
    var file = _CreateSimpleMspFile();
    var raw = _ToRawImage(file);

    Assert.That(raw, Is.Not.Null);
    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(8));
    Assert.That(raw.PixelData, Is.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PatternB_ExplicitWrapper_Works() {
    var file = _CreateSimpleMspFile();
    var rawFromInterface = _ToRawImage(file);
    var rawFromInstance = file.ToRawImage();

    Assert.That(rawFromInterface.Width, Is.EqualTo(rawFromInstance.Width));
    Assert.That(rawFromInterface.Height, Is.EqualTo(rawFromInstance.Height));
    Assert.That(rawFromInterface.Format, Is.EqualTo(rawFromInstance.Format));
    Assert.That(rawFromInterface.PixelData, Is.EqualTo(rawFromInstance.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_CreatesValidFile() {
    var raw = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[4],
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };

    var file = _FromRawImage<MspFile>(raw);
    Assert.That(file, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesNonEmptyOutput() {
    var file = _CreateSimpleMspFile();
    var bytes = _ToBytes(file);
    Assert.That(bytes, Is.Not.Empty);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_FromRawImage_PreservesDimensions() {
    var original = _CreateSimpleMspFile();
    var raw = _ToRawImage(original);
    var reconstructed = _FromRawImage<MspFile>(raw);
    var raw2 = _ToRawImage(reconstructed);

    Assert.That(raw2.Width, Is.EqualTo(raw.Width));
    Assert.That(raw2.Height, Is.EqualTo(raw.Height));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToBytes_FromFile_Preserves() {
    var original = _CreateSimpleMspFile();
    var bytes = _ToBytes(original);
    var tempPath = Path.Combine(Path.GetTempPath(), $"iiff_test_{Guid.NewGuid():N}.msp");
    try {
      File.WriteAllBytes(tempPath, bytes);
      var readBack = _FromFile<MspFile>(new(tempPath));

      Assert.That(readBack, Is.Not.Null);
      var rawOriginal = _ToRawImage(original);
      var rawReadBack = _ToRawImage(readBack);
      Assert.That(rawReadBack.Width, Is.EqualTo(rawOriginal.Width));
      Assert.That(rawReadBack.Height, Is.EqualTo(rawOriginal.Height));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Full_PixelDataPreserved() {
    var raw = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Indexed1,
      PixelData = [0xFF, 0xAA, 0x55, 0x00],
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };

    var file = _FromRawImage<MspFile>(raw);
    var bytes = _ToBytes(file);
    var tempPath = Path.Combine(Path.GetTempPath(), $"iiff_full_{Guid.NewGuid():N}.msp");
    try {
      File.WriteAllBytes(tempPath, bytes);
      var readBack = _FromFile<MspFile>(new(tempPath));
      var raw2 = _ToRawImage(readBack);

      Assert.That(raw2.Width, Is.EqualTo(raw.Width));
      Assert.That(raw2.Height, Is.EqualTo(raw.Height));
      Assert.That(raw2.PixelData, Is.EqualTo(raw.PixelData));
    } finally {
      File.Delete(tempPath);
    }
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;

  private static RawImage _ToRawImage<T>(T file) where T : IImageFileFormat<T>
    => T.ToRawImage(file);

  private static T _FromRawImage<T>(RawImage raw) where T : IImageFileFormat<T>
    => T.FromRawImage(raw);

  private static byte[] _ToBytes<T>(T file) where T : IImageFileFormat<T>
    => T.ToBytes(file);

  private static T _FromFile<T>(FileInfo file) where T : IImageFileFormat<T>
    => T.FromFile(file);
}
