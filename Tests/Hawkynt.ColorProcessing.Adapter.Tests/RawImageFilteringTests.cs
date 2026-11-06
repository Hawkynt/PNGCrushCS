using System;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;

namespace Hawkynt.ColorProcessing.Adapter.Tests;

/// <summary>
/// The colour library's filters and pixel-art doublers, driven over a <see cref="RawImage"/>.
/// </summary>
/// <remarks>
/// As with the quantizers, whether an effect is a good effect is the library's business. These pin
/// the adapter: that both of the library's routes (the five-by-five kernel and the frame kernel) and
/// the doublers run off Windows, that the frame comes back the size it should with the channels in
/// the order they went in, and that the caller's picture is never written through.
/// </remarks>
[TestFixture]
public sealed class RawImageFilteringTests {

  private static RawImage _Gradient(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
        pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
        pixels[at + 2] = (byte)((x + y) * 7 & 0xFF);
        pixels[at + 3] = 255;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  private static RawImage _Flat(int width, int height, byte blue, byte green, byte red, byte alpha = 255) {
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < pixels.Length; i += 4) {
      pixels[i] = blue;
      pixels[i + 1] = green;
      pixels[i + 2] = red;
      pixels[i + 3] = alpha;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void KernelFilter_ChangesThePictureAndLeavesTheSourceAlone() {
    var source = _Gradient(32, 24);
    var before = (byte[])source.PixelData.Clone();

    var result = RawImageEffects.Posterize(source, 2);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(24));
    Assert.That(result.PixelData, Is.Not.EqualTo(before));
    Assert.That(source.PixelData, Is.EqualTo(before));
  }

  [Test]
  [Category("Unit")]
  public void Posterize_ToTwoLevels_LeavesOnlyBlackAndFullScale() {
    var result = RawImageEffects.Posterize(_Gradient(16, 16), 2);

    for (var i = 0; i < result.PixelData.Length; i += 4) {
      Assert.That(result.PixelData[i], Is.EqualTo(0).Or.EqualTo(255));
      Assert.That(result.PixelData[i + 1], Is.EqualTo(0).Or.EqualTo(255));
      Assert.That(result.PixelData[i + 2], Is.EqualTo(0).Or.EqualTo(255));
    }
  }

  /// <summary>A flat colour through a filter that should not move it catches a channel swap.</summary>
  [Test]
  [Category("Unit")]
  public void FrameFilter_OnAFlatColour_KeepsTheChannelsWhereTheyWere() {
    var result = RawImageEffects.Pixelate(_Flat(24, 24, 20, 120, 220), 8);

    Assert.That(result.PixelData[0], Is.EqualTo(20).Within(1));
    Assert.That(result.PixelData[1], Is.EqualTo(120).Within(1));
    Assert.That(result.PixelData[2], Is.EqualTo(220).Within(1));
    Assert.That(result.PixelData[3], Is.EqualTo(255));
  }

  /// <summary>
  /// A frame filter that has nothing to average must hand back every stored level as it was.
  /// </summary>
  /// <remarks>
  /// The frame route goes through linear light and back. The library's own encoder cuts linear light
  /// into 256 steps before compressing it, which turns the whole bottom of the scale into a dozen
  /// levels — a flat blue of 20 came back as 13. This walks every level through a one-pixel pixelate,
  /// which is the identity, so any level the round trip cannot hold shows up as itself.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FrameRoute_HoldsEveryStoredLevel() {
    var pixels = new byte[256 * 4];
    for (var level = 0; level < 256; ++level) {
      pixels[level * 4] = (byte)level;
      pixels[level * 4 + 1] = (byte)(255 - level);
      pixels[level * 4 + 2] = (byte)level;
      pixels[level * 4 + 3] = 255;
    }

    var source = new RawImage { Width = 256, Height = 1, Format = PixelFormat.Bgra32, PixelData = pixels };

    var result = RawImageEffects.Pixelate(source, 1);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FrameFilter_Pixelate_MakesEveryBlockOneColour() {
    var result = RawImageEffects.Pixelate(_Gradient(32, 32), 8);

    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = (y * 32 + x) * 4;
        Assert.That(result.PixelData[at], Is.EqualTo(result.PixelData[0]), $"blue at {x},{y}");
        Assert.That(result.PixelData[at + 1], Is.EqualTo(result.PixelData[1]), $"green at {x},{y}");
      }
  }

  [Test]
  [Category("Unit")]
  public void Doubler_DoublesBothSides_AndKeepsAFlatColour() {
    var result = RawImageEffects.Scale2x(_Flat(5, 3, 10, 20, 30));

    Assert.That(result.Width, Is.EqualTo(10));
    Assert.That(result.Height, Is.EqualTo(6));
    for (var i = 0; i < result.PixelData.Length; i += 4)
      Assert.That(result.PixelData[i..(i + 4)], Is.EqualTo(new byte[] { 10, 20, 30, 255 }));
  }

  [TestCase("Eagle")]
  [TestCase("Xbr")]
  [Category("Unit")]
  public void OtherDoublers_DoubleBothSides(string rule) {
    var source = _Gradient(12, 8);

    var result = rule == "Eagle" ? RawImageEffects.Eagle(source) : RawImageEffects.Xbr(source);

    Assert.That(result.Width, Is.EqualTo(24));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void OpaquePicture_DoesNotGainAnAlphaChannel() {
    var source = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgb24, PixelData = [10, 20, 30, 200, 100, 50] };

    var result = RawImageEffects.Posterize(source, 2);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(result.PixelData, Is.EqualTo(new byte[] { 0, 0, 0, 255, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void PalettePicture_ComesBackAsRgb() {
    var source = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 1],
      Palette = [255, 0, 0, 0, 0, 255],
      PaletteCount = 2,
    };

    var result = RawImageEffects.Sepia(source, 1);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(result.PixelData, Has.Length.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void EveryNamedEffect_Runs() {
    var source = _Gradient(20, 20);

    Assert.Multiple(() => {
      Assert.That(RawImageEffects.MotionBlur(source, 7, 30).PixelData, Is.Not.EqualTo(source.PixelData), "motion blur");
      Assert.That(RawImageEffects.OilPainting(source, 2, 8).PixelData, Is.Not.EqualTo(source.PixelData), "oil painting");
      Assert.That(RawImageEffects.Solarize(source, 0.5).PixelData, Is.Not.EqualTo(source.PixelData), "solarise");
      Assert.That(RawImageEffects.Sepia(source, 1).PixelData, Is.Not.EqualTo(source.PixelData), "sepia");
      Assert.That(RawImageEffects.Duotone(source, 0, 0, 0.2, 1, 0.9, 0.6).PixelData, Is.Not.EqualTo(source.PixelData), "duotone");
      Assert.That(RawImageEffects.ColorBalance(source, (0, 0, 0), (0.5, 0, 0), (0, 0, 0)).PixelData, Is.Not.EqualTo(source.PixelData), "colour balance");
      Assert.That(RawImageEffects.ReduceNoise(source, 0.2, 2).PixelData, Is.Not.EqualTo(source.PixelData), "reduce noise");
    });
  }
}
