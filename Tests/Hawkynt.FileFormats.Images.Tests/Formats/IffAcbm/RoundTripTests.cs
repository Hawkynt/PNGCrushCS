using System;
using System.IO;
using FileFormat.IffAcbm;
using FileFormat.Core;

namespace FileFormat.IffAcbm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_4Plane_PixelDataPreserved() {
    var original = TestHelper.CreateTestFile(16, 4, 4);

    var bytes = IffAcbmWriter.ToBytes(original);
    var restored = IffAcbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_1Plane_Monochrome() {
    var pixelData = new byte[16 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 2);

    var palette = new byte[2 * 3];
    palette[3] = 255; palette[4] = 255; palette[5] = 255;

    var original = new IffAcbmFile {
      Width = 16,
      Height = 2,
      NumPlanes = 1,
      PixelData = pixelData,
      Palette = palette,
    };

    var bytes = IffAcbmWriter.ToBytes(original);
    var restored = IffAcbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(16));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.NumPlanes, Is.EqualTo(1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette_Preserved() {
    var numColors = 16;
    var palette = new byte[numColors * 3];
    for (var i = 0; i < numColors; ++i) {
      palette[i * 3] = (byte)(i * 16);
      palette[i * 3 + 1] = (byte)(255 - i * 16);
      palette[i * 3 + 2] = (byte)(i * 8);
    }

    var pixelData = new byte[8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % numColors);

    var original = new IffAcbmFile {
      Width = 8,
      Height = 4,
      NumPlanes = 4,
      PixelData = pixelData,
      Palette = palette,
      XAspect = 10,
      YAspect = 11,
      PageWidth = 320,
      PageHeight = 200,
    };

    var bytes = IffAcbmWriter.ToBytes(original);
    var restored = IffAcbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.XAspect, Is.EqualTo(10));
      Assert.That(restored.YAspect, Is.EqualTo(11));
      Assert.That(restored.PageWidth, Is.EqualTo(320));
      Assert.That(restored.PageHeight, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new IffAcbmFile {
      Width = 8,
      Height = 4,
      NumPlanes = 4,
      PixelData = new byte[8 * 4],
      Palette = new byte[16 * 3],
    };

    var bytes = IffAcbmWriter.ToBytes(original);
    var restored = IffAcbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = TestHelper.CreateTestFile(32, 16, 4);
    var bytes = IffAcbmWriter.ToBytes(original);

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".acbm");
    try {
      File.WriteAllBytes(tempPath, bytes);
      var restored = IffAcbmReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
        Assert.That(restored.NumPlanes, Is.EqualTo(original.NumPlanes));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Indexed8() {
    var original = TestHelper.CreateTestFile(16, 8, 4);
    var raw = IffAcbmFile.ToRawImage(original);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));

    var restored = IffAcbmFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteAlignedWidth() {
    // Width 13 requires word alignment: (13+15)/16*2 = 2 bytes per plane row
    var original = TestHelper.CreateTestFile(13, 3, 2);

    var bytes = IffAcbmWriter.ToBytes(original);
    var restored = IffAcbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(13));
      Assert.That(restored.Height, Is.EqualTo(3));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8Plane_256Colors() {
    var numColors = 256;
    var palette = new byte[numColors * 3];
    for (var i = 0; i < numColors; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 3 % 256);
    }

    var pixelData = new byte[32 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new IffAcbmFile {
      Width = 32,
      Height = 8,
      NumPlanes = 8,
      PixelData = pixelData,
      Palette = palette,
    };

    var bytes = IffAcbmWriter.ToBytes(original);
    var restored = IffAcbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.NumPlanes, Is.EqualTo(8));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var original = TestHelper.CreateTestFile(320, 200, 4);

    var bytes = IffAcbmWriter.ToBytes(original);
    var restored = IffAcbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
