using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ybm.Tests;

[TestFixture]
public sealed class YbmConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_KnownVector_UsesLeastSignificantPixelBitInsideBigEndianWord() {
    byte[] data = [
      0x21, 0x21,
      0x00, 0x10,
      0x00, 0x01,
      0x80, 0x01,
    ];

    var file = YbmReader.FromBytes(data);
    var raw = YbmFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(16));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0x80, 0x01 }));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.PixelData[0], Is.EqualTo(0), "x=0 must come from word bit 0");
      Assert.That(raw.PixelData[15 * 3], Is.EqualTo(0), "x=15 must come from word bit 15");
      Assert.That(raw.PixelData[1 * 3], Is.EqualTo(255));
      Assert.That(raw.PixelData[14 * 3], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_KnownVector_PreservesBigEndianWordBytes() {
    var file = new YbmFile {
      Width = 16,
      Height = 1,
      RasterData = [0x80, 0x01],
    };

    var data = YbmWriter.ToBytes(file);

    Assert.That(data, Is.EqualTo(new byte[] {
      0x21, 0x21,
      0x00, 0x10,
      0x00, 0x01,
      0x80, 0x01,
    }));
  }

  [Test]
  [Category("Unit")]
  public void Raster_NonWordAlignedWidth_IsPaddedPerRow() {
    var file = new YbmFile {
      Width = 17,
      Height = 2,
      RasterData = [
        0x00, 0x01, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x01,
      ],
    };

    var data = YbmWriter.ToBytes(file);
    var decoded = YbmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(YbmFile.GetRowStride(17), Is.EqualTo(4));
      Assert.That(decoded.Width, Is.EqualTo(17));
      Assert.That(decoded.Height, Is.EqualTo(2));
      Assert.That(decoded.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedRaster_IsRejected() {
    byte[] data = [
      0x21, 0x21,
      0x00, 0x11,
      0x00, 0x01,
      0x00, 0x00, 0x00,
    ];

    var exception = Assert.Throws<InvalidDataException>(() => YbmReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("Truncated YBM raster"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TrailingRasterData_IsRejected() {
    byte[] data = [
      0x21, 0x21,
      0x00, 0x01,
      0x00, 0x01,
      0x00, 0x00, 0x00,
    ];

    var exception = Assert.Throws<InvalidDataException>(() => YbmReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("Unexpected trailing YBM data"));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThresholdsBlackAndWhiteIntoYbmBits() {
    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0, 0, 0, 255, 255, 255],
    };

    var file = YbmFile.FromRawImage(raw);
    var roundTrip = YbmFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0x00, 0x01 }));
      Assert.That(roundTrip.PixelData, Is.EqualTo(raw.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_DimensionOutsideSignedShortRange_IsRejected() {
    var file = new YbmFile {
      Width = 32768,
      Height = 1,
      RasterData = new byte[4096],
    };

    Assert.Throws<ArgumentOutOfRangeException>(() => YbmWriter.ToBytes(file));
  }
}
