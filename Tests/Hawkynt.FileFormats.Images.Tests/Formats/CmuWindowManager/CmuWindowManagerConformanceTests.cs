using System.IO;
using FileFormat.Core;

namespace FileFormat.CmuWindowManager.Tests;

[TestFixture]
public sealed class CmuWindowManagerConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_KnownVector_UsesBigEndianHeaderAndZeroForBlack() {
    byte[] data = [
      0xF1, 0x00, 0x40, 0xBB,
      0x00, 0x00, 0x00, 0x0A,
      0x00, 0x00, 0x00, 0x01,
      0x00, 0x01,
      0x7F, 0xBF,
    ];

    var file = CmuWindowManagerReader.FromBytes(data);
    var raw = CmuWindowManagerFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(10));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.Depth, Is.EqualTo(1));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0x7F, 0xBF }));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.PixelData[0], Is.EqualTo(0), "x=0 is encoded by a zero bit");
      Assert.That(raw.PixelData[1 * 3], Is.EqualTo(255));
      Assert.That(raw.PixelData[8 * 3], Is.EqualTo(255));
      Assert.That(raw.PixelData[9 * 3], Is.EqualTo(0), "x=9 is encoded by a zero bit");
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_KnownVector_PreservesCanonicalHeaderAndRaster() {
    var file = new CmuWindowManagerFile {
      Width = 10,
      Height = 1,
      Depth = 1,
      RasterData = [0x7F, 0xBF],
    };

    var data = CmuWindowManagerWriter.ToBytes(file);

    Assert.That(data, Is.EqualTo(new byte[] {
      0xF1, 0x00, 0x40, 0xBB,
      0x00, 0x00, 0x00, 0x0A,
      0x00, 0x00, 0x00, 0x01,
      0x00, 0x01,
      0x7F, 0xBF,
    }));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsUnusedRowBitsToWhite() {
    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0, 0, 0, 255, 255, 255],
    };

    var file = CmuWindowManagerFile.FromRawImage(raw);
    var roundTrip = CmuWindowManagerFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0x7F }));
      Assert.That(roundTrip.PixelData, Is.EqualTo(raw.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_DepthOtherThanOne_IsRejected() {
    byte[] data = [
      0xF1, 0x00, 0x40, 0xBB,
      0x00, 0x00, 0x00, 0x01,
      0x00, 0x00, 0x00, 0x01,
      0x00, 0x02,
      0xFF,
    ];

    var exception = Assert.Throws<InvalidDataException>(() => CmuWindowManagerReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("depth must be 1"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedRaster_IsRejected() {
    byte[] data = [
      0xF1, 0x00, 0x40, 0xBB,
      0x00, 0x00, 0x00, 0x09,
      0x00, 0x00, 0x00, 0x01,
      0x00, 0x01,
      0xFF,
    ];

    var exception = Assert.Throws<InvalidDataException>(() => CmuWindowManagerReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("Truncated CMU window-manager raster"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TrailingData_IsRejected() {
    byte[] data = [
      0xF1, 0x00, 0x40, 0xBB,
      0x00, 0x00, 0x00, 0x01,
      0x00, 0x00, 0x00, 0x01,
      0x00, 0x01,
      0xFF, 0x00,
    ];

    var exception = Assert.Throws<InvalidDataException>(() => CmuWindowManagerReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("Unexpected trailing CMU window-manager data"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_WrongDepth_IsRejected() {
    var file = new CmuWindowManagerFile {
      Width = 1,
      Height = 1,
      Depth = 2,
      RasterData = [0xFF],
    };

    Assert.Throws<ArgumentOutOfRangeException>(() => CmuWindowManagerWriter.ToBytes(file));
  }
}
