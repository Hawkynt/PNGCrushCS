using System;
using System.IO;
using System.Linq;
using FileFormat.Apng;
using FileFormat.Png;

namespace FileFormat.Apng.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleFrame() {
    var pixelData = new byte[4][];
    for (var y = 0; y < 4; ++y) {
      pixelData[y] = new byte[12];
      for (var x = 0; x < 12; ++x)
        pixelData[y][x] = (byte)((y * 12 + x) * 7);
    }

    var frame = new ApngFrame {
      Width = 4,
      Height = 4,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 100,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = pixelData
    };

    var original = new ApngFile {
      Width = 4,
      Height = 4,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      NumPlays = 0,
      Frames = [frame]
    };

    var bytes = ApngWriter.ToBytes(original);
    var restored = ApngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.ColorType, Is.EqualTo(PngColorType.RGB));
    Assert.That(restored.Frames, Has.Count.EqualTo(1));
    Assert.That(restored.Frames[0].Width, Is.EqualTo(4));
    Assert.That(restored.Frames[0].Height, Is.EqualTo(4));

    for (var y = 0; y < 4; ++y)
      Assert.That(restored.Frames[0].PixelData[y], Is.EqualTo(pixelData[y]), $"Frame 0 scanline {y} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoFrames() {
    var pixels0 = new byte[2][];
    var pixels1 = new byte[2][];
    for (var y = 0; y < 2; ++y) {
      pixels0[y] = new byte[6];
      pixels1[y] = new byte[6];
      for (var x = 0; x < 6; ++x) {
        pixels0[y][x] = (byte)(y * 6 + x);
        pixels1[y][x] = (byte)(255 - y * 6 - x);
      }
    }

    var frame0 = new ApngFrame {
      Width = 2,
      Height = 2,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 100,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = pixels0
    };

    var frame1 = new ApngFrame {
      Width = 2,
      Height = 2,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 200,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.Background,
      BlendOp = ApngBlendOp.Over,
      PixelData = pixels1
    };

    var original = new ApngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      NumPlays = 3,
      Frames = [frame0, frame1]
    };

    var bytes = ApngWriter.ToBytes(original);
    var restored = ApngReader.FromBytes(bytes);

    Assert.That(restored.Frames, Has.Count.EqualTo(2));
    Assert.That(restored.NumPlays, Is.EqualTo(3));

    for (var y = 0; y < 2; ++y) {
      Assert.That(restored.Frames[0].PixelData[y], Is.EqualTo(pixels0[y]), $"Frame 0 scanline {y} mismatch");
      Assert.That(restored.Frames[1].PixelData[y], Is.EqualTo(pixels1[y]), $"Frame 1 scanline {y} mismatch");
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DisposeOps_Preserved() {
    var pixels = new byte[][] { new byte[6], new byte[6] };

    var frames = new[] {
      new ApngFrame {
        Width = 2, Height = 2, DisposeOp = ApngDisposeOp.None, BlendOp = ApngBlendOp.Source,
        DelayNumerator = 100, DelayDenominator = 1000, PixelData = pixels
      },
      new ApngFrame {
        Width = 2, Height = 2, DisposeOp = ApngDisposeOp.Background, BlendOp = ApngBlendOp.Source,
        DelayNumerator = 100, DelayDenominator = 1000, PixelData = pixels
      },
      new ApngFrame {
        Width = 2, Height = 2, DisposeOp = ApngDisposeOp.Previous, BlendOp = ApngBlendOp.Source,
        DelayNumerator = 100, DelayDenominator = 1000, PixelData = pixels
      }
    };

    var original = new ApngFile {
      Width = 2, Height = 2, BitDepth = 8, ColorType = PngColorType.RGB, Frames = frames
    };

    var bytes = ApngWriter.ToBytes(original);
    var restored = ApngReader.FromBytes(bytes);

    Assert.That(restored.Frames[0].DisposeOp, Is.EqualTo(ApngDisposeOp.None));
    Assert.That(restored.Frames[1].DisposeOp, Is.EqualTo(ApngDisposeOp.Background));
    Assert.That(restored.Frames[2].DisposeOp, Is.EqualTo(ApngDisposeOp.Previous));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BlendOps_Preserved() {
    var pixels = new byte[][] { new byte[6], new byte[6] };

    var frames = new[] {
      new ApngFrame {
        Width = 2, Height = 2, DisposeOp = ApngDisposeOp.None, BlendOp = ApngBlendOp.Source,
        DelayNumerator = 100, DelayDenominator = 1000, PixelData = pixels
      },
      new ApngFrame {
        Width = 2, Height = 2, DisposeOp = ApngDisposeOp.None, BlendOp = ApngBlendOp.Over,
        DelayNumerator = 100, DelayDenominator = 1000, PixelData = pixels
      }
    };

    var original = new ApngFile {
      Width = 2, Height = 2, BitDepth = 8, ColorType = PngColorType.RGB, Frames = frames
    };

    var bytes = ApngWriter.ToBytes(original);
    var restored = ApngReader.FromBytes(bytes);

    Assert.That(restored.Frames[0].BlendOp, Is.EqualTo(ApngBlendOp.Source));
    Assert.That(restored.Frames[1].BlendOp, Is.EqualTo(ApngBlendOp.Over));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[][] { new byte[6], new byte[6] };
    pixels[0][0] = 42;
    pixels[1][3] = 99;

    var frame = new ApngFrame {
      Width = 2,
      Height = 2,
      DelayNumerator = 100,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = pixels
    };

    var original = new ApngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      NumPlays = 0,
      Frames = [frame]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".apng");
    try {
      var bytes = ApngWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = ApngReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.Frames, Has.Count.EqualTo(1));
      Assert.That(restored.Frames[0].PixelData[0][0], Is.EqualTo(42));
      Assert.That(restored.Frames[0].PixelData[1][3], Is.EqualTo(99));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette() {
    var palette = new byte[12];
    palette[0] = 255; palette[1] = 0; palette[2] = 0;   // red
    palette[3] = 0; palette[4] = 255; palette[5] = 0;   // green
    palette[6] = 0; palette[7] = 0; palette[8] = 255;   // blue
    palette[9] = 255; palette[10] = 255; palette[11] = 0; // yellow

    var pixelData = new byte[2][];
    pixelData[0] = [0, 1];
    pixelData[1] = [2, 3];

    var frame = new ApngFrame {
      Width = 2,
      Height = 2,
      DelayNumerator = 100,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = pixelData
    };

    var original = new ApngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.Palette,
      Palette = palette,
      NumPlays = 0,
      Frames = [frame]
    };

    var bytes = ApngWriter.ToBytes(original);
    var restored = ApngReader.FromBytes(bytes);

    Assert.That(restored.ColorType, Is.EqualTo(PngColorType.Palette));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette!, Is.EqualTo(palette));
    Assert.That(restored.Frames, Has.Count.EqualTo(1));
    Assert.That(restored.Frames[0].PixelData[0], Is.EqualTo(pixelData[0]));
    Assert.That(restored.Frames[0].PixelData[1], Is.EqualTo(pixelData[1]));
  }
}
