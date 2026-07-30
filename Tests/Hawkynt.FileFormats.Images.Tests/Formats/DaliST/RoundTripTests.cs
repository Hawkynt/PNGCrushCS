using System;
using System.IO;
using FileFormat.DaliST;

namespace FileFormat.DaliST.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_LowRes_PreservesAllFields() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DaliSTWriter.ToBytes(original);
    var restored = DaliSTReader.FromBytes(bytes, DaliSTResolution.Low);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Resolution, Is.EqualTo(original.Resolution));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MediumRes_PreservesAllFields() {
    var palette = new short[16];
    palette[0] = 0x000;
    palette[1] = 0x700;
    palette[2] = 0x070;
    palette[3] = 0x007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new DaliSTFile {
      Width = 640,
      Height = 200,
      Resolution = DaliSTResolution.Medium,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DaliSTWriter.ToBytes(original);
    var restored = DaliSTReader.FromBytes(bytes, DaliSTResolution.Medium);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.Resolution, Is.EqualTo(DaliSTResolution.Medium));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_HighRes_PreservesAllFields() {
    var palette = new short[16];
    palette[0] = 0x000;
    palette[1] = 0x777;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

    var original = new DaliSTFile {
      Width = 640,
      Height = 400,
      Resolution = DaliSTResolution.High,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DaliSTWriter.ToBytes(original);
    var restored = DaliSTReader.FromBytes(bytes, DaliSTResolution.High);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(400));
      Assert.That(restored.Resolution, Is.EqualTo(DaliSTResolution.High));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var palette = new short[16];
    palette[0] = 0x777;

    var pixelData = new byte[32000];
    pixelData[0] = 0xFF;

    var original = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sd0");
    try {
      var bytes = DaliSTWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = DaliSTReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Palette[0], Is.EqualTo(0x777));
        Assert.That(restored.PixelData[0], Is.EqualTo(0xFF));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var original = new DaliSTFile {
      Width = 320,
      Height = 200,
      Resolution = DaliSTResolution.Low,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = DaliSTWriter.ToBytes(original);
    var restored = DaliSTReader.FromBytes(bytes, DaliSTResolution.Low);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.All.EqualTo((short)0));
      Assert.That(restored.PixelData, Is.All.EqualTo((byte)0));
    });
  }
}
