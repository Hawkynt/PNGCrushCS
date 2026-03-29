using System;
using System.IO;
using FileFormat.DrawIt;

namespace FileFormat.DrawIt.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesAllFields() {
    var palette = new byte[768];
    for (var i = 0; i < 768; ++i)
      palette[i] = (byte)(i % 256);

    var pixelData = new byte[64000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new DrawItFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DrawItWriter.ToBytes(original);
    var restored = DrawItReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage_PreservesData() {
    var palette = new byte[768];
    palette[0] = 255;

    var pixelData = new byte[] { 0, 1, 2, 3 };

    var original = new DrawItFile {
      Width = 2,
      Height = 2,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DrawItWriter.ToBytes(original);
    var restored = DrawItReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.Palette[0], Is.EqualTo(255));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var palette = new byte[768];
    palette[0] = 0xAA;

    var pixelData = new byte[100];
    pixelData[0] = 42;

    var original = new DrawItFile {
      Width = 10,
      Height = 10,
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dit");
    try {
      var bytes = DrawItWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = DrawItReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(10));
        Assert.That(restored.Height, Is.EqualTo(10));
        Assert.That(restored.Palette[0], Is.EqualTo(0xAA));
        Assert.That(restored.PixelData[0], Is.EqualTo(42));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var original = new DrawItFile {
      Width = 4,
      Height = 4,
      Palette = new byte[768],
      PixelData = new byte[16]
    };

    var bytes = DrawItWriter.ToBytes(original);
    var restored = DrawItReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.All.EqualTo((byte)0));
      Assert.That(restored.PixelData, Is.All.EqualTo((byte)0));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage_PreservesData() {
    var palette = new byte[768];
    var pixelData = new byte[640 * 480];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i & 0xFF);

    var original = new DrawItFile {
      Width = 640,
      Height = 480,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DrawItWriter.ToBytes(original);
    var restored = DrawItReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(480));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
