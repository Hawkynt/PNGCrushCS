using System;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

[TestFixture]
public sealed class IlbmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IlbmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IlbmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ilbm"));
    Assert.Throws<FileNotFoundException>(() => IlbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IlbmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => IlbmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[12];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'Z';
    Assert.Throws<InvalidDataException>(() => IlbmReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    var bad = new byte[40];
    bad[0] = (byte)'F'; bad[1] = (byte)'O'; bad[2] = (byte)'R'; bad[3] = (byte)'M';
    // size (BE)
    bad[4] = 0; bad[5] = 0; bad[6] = 0; bad[7] = 20;
    // wrong form type
    bad[8] = (byte)'A'; bad[9] = (byte)'N'; bad[10] = (byte)'I'; bad[11] = (byte)'M';
    Assert.Throws<InvalidDataException>(() => IlbmReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid4Plane_ParsesCorrectly() {
    var data = _BuildMinimalIlbm(8, 2, 4, IlbmCompression.None);
    var result = IlbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.NumPlanes, Is.EqualTo(4));
      Assert.That(result.Compression, Is.EqualTo(IlbmCompression.None));
      Assert.That(result.PixelData.Length, Is.EqualTo(8 * 2));
      Assert.That(result.Palette, Is.Not.Null);
      Assert.That(result.Palette!.Length, Is.EqualTo(16 * 3)); // 2^4 = 16 colors
    });
  }

  internal static byte[] _BuildMinimalIlbm(int width, int height, int numPlanes, IlbmCompression compression) {
    var numColors = 1 << numPlanes;
    var palette = new byte[numColors * 3];
    for (var i = 0; i < numColors; ++i) {
      palette[i * 3] = (byte)(i * 17 % 256);
      palette[i * 3 + 1] = (byte)(i * 31 % 256);
      palette[i * 3 + 2] = (byte)(i * 53 % 256);
    }

    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % numColors);

    var file = new IlbmFile {
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      Compression = compression,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = 10,
      YAspect = 11,
      PageWidth = (short)width,
      PageHeight = (short)height,
      PixelData = pixelData,
      Palette = palette
    };

    return IlbmWriter.ToBytes(file);
  }
}
