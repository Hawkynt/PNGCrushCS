using System;
using System.IO;
using FileFormat.IffPbm;

namespace FileFormat.IffPbm.Tests;

[TestFixture]
public sealed class IffPbmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffPbmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffPbmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pbm"));
    Assert.Throws<FileNotFoundException>(() => IffPbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffPbmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => IffPbmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[12];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'Z';
    Assert.Throws<InvalidDataException>(() => IffPbmReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var bad = new byte[40];
    bad[0] = (byte)'F'; bad[1] = (byte)'O'; bad[2] = (byte)'R'; bad[3] = (byte)'M';
    bad[4] = 0; bad[5] = 0; bad[6] = 0; bad[7] = 20;
    bad[8] = (byte)'I'; bad[9] = (byte)'L'; bad[10] = (byte)'B'; bad[11] = (byte)'M';
    Assert.Throws<InvalidDataException>(() => IffPbmReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUncompressed_ParsesCorrectly() {
    var data = _BuildMinimalPbm(8, 4, IffPbmCompression.None);
    var result = IffPbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.Compression, Is.EqualTo(IffPbmCompression.None));
      Assert.That(result.PixelData.Length, Is.EqualTo(8 * 4));
      Assert.That(result.Palette, Is.Not.Null);
      Assert.That(result.Palette!.Length, Is.EqualTo(256 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidCompressed_ParsesCorrectly() {
    var data = _BuildMinimalPbm(16, 8, IffPbmCompression.ByteRun1);
    var result = IffPbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(8));
      Assert.That(result.Compression, Is.EqualTo(IffPbmCompression.ByteRun1));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildMinimalPbm(8, 4, IffPbmCompression.None);
    using var ms = new MemoryStream(data);
    var result = IffPbmReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var file = new IffPbmFile {
      Width = 4,
      Height = 2,
      Compression = IffPbmCompression.None,
      PixelData = [0, 1, 2, 3, 4, 5, 6, 7],
      Palette = new byte[256 * 3],
      XAspect = 1,
      YAspect = 1,
      PageWidth = 4,
      PageHeight = 2,
    };
    var bytes = IffPbmWriter.ToBytes(file);
    var result = IffPbmReader.FromBytes(bytes);

    Assert.That(result.PixelData, Is.EqualTo(file.PixelData));
  }

  internal static byte[] _BuildMinimalPbm(int width, int height, IffPbmCompression compression) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)(i * 17 % 256);
      palette[i * 3 + 1] = (byte)(i * 31 % 256);
      palette[i * 3 + 2] = (byte)(i * 53 % 256);
    }

    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new IffPbmFile {
      Width = width,
      Height = height,
      Compression = compression,
      TransparentColor = 0,
      XAspect = 10,
      YAspect = 11,
      PageWidth = width,
      PageHeight = height,
      PixelData = pixelData,
      Palette = palette,
    };

    return IffPbmWriter.ToBytes(file);
  }
}
