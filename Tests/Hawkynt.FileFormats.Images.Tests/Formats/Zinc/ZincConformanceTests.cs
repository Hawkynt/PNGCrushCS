using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Zinc.Tests;

[TestFixture]
public sealed class ZincConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_KnownVector_UsesMostSignificantWordBitForLeftmostPixel() {
    var data = Encoding.ASCII.GetBytes("""
      USHORT sample[] = {
        16
        1
       0xc001};
      """);

    var file = ZincReader.FromBytes(data);
    var raw = ZincFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Name, Is.EqualTo("sample"));
      Assert.That(file.Width, Is.EqualTo(16));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.RasterWords, Is.EqualTo(new ushort[] { 0xc001 }));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PixelData[0], Is.EqualTo(1), "x=0 must come from word bit 15");
      Assert.That(raw.PixelData[1], Is.EqualTo(1), "x=1 must come from word bit 14");
      Assert.That(raw.PixelData[2], Is.EqualTo(0));
      Assert.That(raw.PixelData[14], Is.EqualTo(0));
      Assert.That(raw.PixelData[15], Is.EqualTo(1), "x=15 must come from word bit 0");
      Assert.That(raw.Palette, Is.EqualTo(new byte[] { 255, 255, 255, 0, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_KnownVector_MatchesHistoricalPbmToZincLayout() {
    var file = new ZincFile {
      Width = 16,
      Height = 1,
      Name = "sample",
      RasterWords = [0xc001],
    };

    var data = ZincWriter.ToBytes(file);

    const string expected = "USHORT sample[] = {\n  16\n  1\n 0xc001};\n";
    Assert.That(Encoding.ASCII.GetString(data), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void Raster_NonWordAlignedWidth_IsPaddedPerRow() {
    var file = new ZincFile {
      Width = 17,
      Height = 2,
      Name = "padded",
      RasterWords = [0x8000, 0x8000, 0x0001, 0x8000],
    };

    var encoded = ZincWriter.ToBytes(file);
    var decoded = ZincReader.FromBytes(encoded);

    Assert.Multiple(() => {
      Assert.That(ZincFile.GetWordsPerRow(17), Is.EqualTo(2));
      Assert.That(decoded.Width, Is.EqualTo(17));
      Assert.That(decoded.Height, Is.EqualTo(2));
      Assert.That(decoded.RasterWords, Is.EqualTo(file.RasterWords));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_CommaSeparatedArray_IsAccepted() {
    var data = Encoding.ASCII.GetBytes("USHORT sample[] = { 2, 1, 0x8000 };\n");

    var file = ZincReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.RasterWords, Is.EqualTo(new ushort[] { 0x8000 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedRaster_IsRejected() {
    var data = Encoding.ASCII.GetBytes("USHORT sample[] = { 17 1 0x8000 };\n");

    var exception = Assert.Throws<InvalidDataException>(() => ZincReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("Truncated Zinc raster"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TrailingRasterData_IsRejected() {
    var data = Encoding.ASCII.GetBytes("USHORT sample[] = { 1 1 0x8000, 0x0000 };\n");

    var exception = Assert.Throws<InvalidDataException>(() => ZincReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("Unexpected trailing Zinc raster data"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ExcessiveDimensions_AreRejectedBeforeAllocation() {
    var data = Encoding.ASCII.GetBytes("USHORT huge[] = { 65535 65535 };\n");

    var exception = Assert.Throws<InvalidDataException>(() => ZincReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("safety limit"));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThresholdsBlackIntoMostSignificantBits() {
    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0, 0, 0, 255, 255, 255],
    };

    var file = ZincFile.FromRawImage(raw);
    var decoded = ZincFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.RasterWords, Is.EqualTo(new ushort[] { 0x8000 }));
      Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 1, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_InvalidIdentifier_IsRejected() {
    var file = new ZincFile {
      Width = 1,
      Height = 1,
      Name = "not-valid",
      RasterWords = [0],
    };

    Assert.Throws<ArgumentException>(() => ZincWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void Writer_DimensionOutsideUShortRange_IsRejected() {
    var file = new ZincFile {
      Width = 65536,
      Height = 1,
      Name = "too_wide",
      RasterWords = [],
    };

    Assert.Throws<ArgumentOutOfRangeException>(() => ZincWriter.ToBytes(file));
  }
}
