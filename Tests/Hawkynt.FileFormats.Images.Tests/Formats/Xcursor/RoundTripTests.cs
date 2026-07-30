using System;
using System.IO;
using FileFormat.Xcursor;
using FileFormat.Core;

namespace FileFormat.Xcursor.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Basic() {
    var pixelData = new byte[2 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new XcursorFile {
      Width = 2,
      Height = 2,
      XHot = 0,
      YHot = 0,
      NominalSize = 32,
      Delay = 0,
      PixelData = pixelData,
    };

    var bytes = XcursorWriter.ToBytes(original);
    var restored = XcursorReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.XHot, Is.EqualTo(original.XHot));
    Assert.That(restored.YHot, Is.EqualTo(original.YHot));
    Assert.That(restored.NominalSize, Is.EqualTo(original.NominalSize));
    Assert.That(restored.Delay, Is.EqualTo(original.Delay));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_HotspotPreserved() {
    var original = new XcursorFile {
      Width = 4,
      Height = 4,
      XHot = 2,
      YHot = 3,
      NominalSize = 32,
      Delay = 50,
      PixelData = new byte[4 * 4 * 4],
    };

    var bytes = XcursorWriter.ToBytes(original);
    var restored = XcursorReader.FromBytes(bytes);

    Assert.That(restored.XHot, Is.EqualTo(2));
    Assert.That(restored.YHot, Is.EqualTo(3));
    Assert.That(restored.Delay, Is.EqualTo(50));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xcur");
    try {
      var pixelData = new byte[3 * 2 * 4];
      for (var i = 0; i < pixelData.Length; ++i)
        pixelData[i] = (byte)(i * 7 % 256);

      var original = new XcursorFile {
        Width = 3,
        Height = 2,
        XHot = 1,
        YHot = 1,
        NominalSize = 24,
        Delay = 100,
        PixelData = pixelData,
      };

      var bytes = XcursorWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = XcursorReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.XHot, Is.EqualTo(original.XHot));
      Assert.That(restored.YHot, Is.EqualTo(original.YHot));
      Assert.That(restored.NominalSize, Is.EqualTo(original.NominalSize));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_OpaquePixels() {
    var original = new XcursorFile {
      Width = 2,
      Height = 1,
      NominalSize = 32,
      PixelData = [
        0x80, 0x40, 0xC0, 0xFF,
        0x10, 0x20, 0x30, 0xFF,
      ],
    };

    var raw = XcursorFile.ToRawImage(original);
    var restored = XcursorFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_TransparentPixels() {
    var original = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = [0x00, 0x00, 0x00, 0x00],
    };

    var raw = XcursorFile.ToRawImage(original);
    var restored = XcursorFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 32;
    var height = 32;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new XcursorFile {
      Width = width,
      Height = height,
      XHot = 16,
      YHot = 16,
      NominalSize = 32,
      Delay = 0,
      PixelData = pixelData,
    };

    var bytes = XcursorWriter.ToBytes(original);
    var restored = XcursorReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new XcursorFile {
      Width = 1,
      Height = 1,
      NominalSize = 32,
      PixelData = [0xFF, 0x00, 0x80, 0xFF],
    };

    var bytes = XcursorWriter.ToBytes(original);
    var restored = XcursorReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new XcursorFile {
      Width = 2,
      Height = 2,
      NominalSize = 32,
      PixelData = new byte[2 * 2 * 4],
    };

    var bytes = XcursorWriter.ToBytes(original);
    var restored = XcursorReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
