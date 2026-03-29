using System;
using FileFormat.Wsq;
using FileFormat.Core;

namespace FileFormat.Wsq.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void WsqFile_DefaultWidth_IsZero() {
    var file = new WsqFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void WsqFile_DefaultHeight_IsZero() {
    var file = new WsqFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void WsqFile_BitDepth_Always8() {
    var file = new WsqFile();
    Assert.That(file.BitDepth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void WsqFile_DefaultPpi_Is500() {
    var file = new WsqFile();
    Assert.That(file.Ppi, Is.EqualTo(500));
  }

  [Test]
  [Category("Unit")]
  public void WsqFile_DefaultCompressionRatio() {
    var file = new WsqFile();
    Assert.That(file.CompressionRatio, Is.EqualTo(0.75));
  }

  [Test]
  [Category("Unit")]
  public void WsqFile_DefaultPixelData_IsEmpty() {
    var file = new WsqFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void WsqFile_PrimaryExtension_IsWsq() {
    Assert.That(_GetPrimaryExtension<WsqFile>(), Is.EqualTo(".wsq"));
  }

  [Test]
  [Category("Unit")]
  public void WsqFile_FileExtensions_ContainsWsq() {
    Assert.That(_GetFileExtensions<WsqFile>(), Contains.Item(".wsq"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WsqFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WsqFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[16 * 16 * 3]
    };

    Assert.Throws<ArgumentException>(() => WsqFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsGray8() {
    var file = new WsqFile {
      Width = 4,
      Height = 4,
      PixelData = new byte[16]
    };

    var raw = WsqFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_CreatesCorrectFile() {
    var raw = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Gray8,
      PixelData = new byte[64]
    };
    raw.PixelData[0] = 200;

    var file = WsqFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(8));
    Assert.That(file.PixelData[0], Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void NumSubbands_Is16() {
    Assert.That(WsqWavelet.NUM_SUBBANDS, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void NumLevels_Is5() {
    Assert.That(WsqWavelet.NUM_LEVELS, Is.EqualTo(5));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
