using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MicroDesignGrf.Tests;

[TestFixture]
public sealed class MicroDesignGrfConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_KnownVector_UsesLittleEndianDimensionsAndIgnoresPaddingBits() {
    byte[] data = [
      0x0A, 0x00, 0x02, 0x00,
      0xA5, 0xFF,
      0x40, 0x3F,
    ];

    var file = MicroDesignGrfReader.FromBytes(data);
    var raw = MicroDesignGrfFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(10));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xA5, 0xFF, 0x40, 0x3F }));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.Palette, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
      Assert.That(raw.PixelData, Is.EqualTo(new byte[] {
        1, 0, 1, 0, 0, 1, 0, 1, 1, 1,
        0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_KnownVector_PreservesPackedRowsIncludingPadding() {
    var file = new MicroDesignGrfFile {
      Width = 10,
      Height = 2,
      RasterData = [0xA5, 0xFF, 0x40, 0x3F],
    };

    Assert.That(MicroDesignGrfWriter.ToBytes(file), Is.EqualTo(new byte[] {
      0x0A, 0x00, 0x02, 0x00,
      0xA5, 0xFF,
      0x40, 0x3F,
    }));
  }

  [Test]
  [Category("Unit")]
  public void RawImageConversion_UsesMicroDesignOneMeansWhiteConvention() {
    var raw = new RawImage {
      Width = 8,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [
        255,255,255, 0,0,0, 255,255,255, 0,0,0, 0,0,0, 255,255,255, 0,0,0, 255,255,255,
        0,0,0, 255,255,255, 0,0,0, 255,255,255, 255,255,255, 0,0,0, 255,255,255, 0,0,0,
      ],
    };

    var file = MicroDesignGrfFile.FromRawImage(raw);
    var decoded = MicroDesignGrfFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xA5, 0x5A }));
      Assert.That(decoded.PixelData, Is.EqualTo(new byte[] {
        1,0,1,0,0,1,0,1,
        0,1,0,1,1,0,1,0,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriterReader_RoundTripsArbitraryPaddingBits() {
    var file = new MicroDesignGrfFile {
      Width = 9,
      Height = 3,
      RasterData = [0x12, 0xFF, 0x34, 0x7F, 0x56, 0x3F],
    };

    var decoded = MicroDesignGrfReader.FromBytes(MicroDesignGrfWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(file.Width));
      Assert.That(decoded.Height, Is.EqualTo(file.Height));
      Assert.That(decoded.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedHeader_IsRejected() {
    Assert.Throws<InvalidDataException>(() => MicroDesignGrfReader.FromBytes([1, 0, 1]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedRaster_IsRejected() {
    Assert.Throws<InvalidDataException>(() => MicroDesignGrfReader.FromBytes([
      16, 0, 2, 0,
      0xAA, 0x55, 0xAA,
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TrailingData_IsRejected() {
    Assert.Throws<InvalidDataException>(() => MicroDesignGrfReader.FromBytes([
      8, 0, 1, 0,
      0xAA, 0x55,
    ]));
  }

  [TestCase(0, 1)]
  [TestCase(1, 0)]
  [Category("Unit")]
  public void Reader_ZeroDimension_IsRejected(int width, int height) {
    var data = new byte[4];
    data[0] = (byte)width;
    data[2] = (byte)height;

    Assert.Throws<InvalidDataException>(() => MicroDesignGrfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ExcessiveDimensions_AreRejectedBeforeLengthValidation() {
    Assert.Throws<InvalidDataException>(() => MicroDesignGrfReader.FromBytes([
      0xFF, 0xFF, 0xFF, 0xFF,
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RasterLengthMustMatchDimensionsExactly() {
    var file = new MicroDesignGrfFile {
      Width = 9,
      Height = 2,
      RasterData = [0xAA, 0x00, 0x55],
    };

    Assert.Throws<ArgumentException>(() => MicroDesignGrfWriter.ToBytes(file));
  }
}
