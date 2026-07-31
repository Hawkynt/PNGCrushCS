using System;
using System.Linq;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;

namespace Optimizer.Png.Tests;

/// <summary>
/// The colour reduction the PNG optimizer reaches for, by the names it uses.
/// </summary>
/// <remarks>
/// This used to test a reflection-based dispatch that took a bitmap. That dispatch is gone: the
/// choice is now a generated switch over the types themselves, which is what lets the result be
/// trimmed and compiled ahead of time. What still has to hold is that the names the optimizer
/// passes — the older, registry-style spellings — keep meaning what they meant.
/// </remarks>
[TestFixture]
public sealed class ReduceColorsDispatchTests {

  [Test]
  [Category("Unit")]
  public void TheQuantizersTheOptimizerNamesAreAvailable() {
    var names = ColorReductionDispatch.QuantizerNames.ToList();

    Assert.Multiple(() => {
      Assert.That(names, Is.Not.Empty);
      Assert.That(names, Does.Contain("WuQuantizer"));
      Assert.That(names, Does.Contain("OctreeQuantizer"));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDitherersTheOptimizerNamesAreAvailable() {
    var names = ColorReductionDispatch.DithererNames.ToList();

    Assert.Multiple(() => {
      Assert.That(names, Is.Not.Empty);
      Assert.That(names, Does.Contain("ErrorDiffusion.FloydSteinberg"));
      Assert.That(names, Does.Contain("NoDithering.Instance"));
    });
  }

  [TestCase("Wu", "NoDithering_Instance")]
  [TestCase("Octree", "ErrorDiffusion_FloydSteinberg")]
  [TestCase("Median Cut", "NoDithering_Instance")]
  [Category("Unit")]
  public void TheOptimizersOwnNames_StillProduceAnIndexedPicture(string quantizer, string ditherer) {
    var result = ColorReductionDispatch.ReduceByName(_Sample(), quantizer, ditherer, 16);

    Assert.Multiple(() => {
      Assert.That(result.Image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(result.Image.PaletteCount, Is.EqualTo(16));
      Assert.That(result.Image.PixelData.All(i => i < 16), Is.True, "used an index past the palette");
    });
  }

  [Test]
  [Category("Unit")]
  public void AnUnknownNameIsRefused() {
    Assert.Multiple(() => {
      Assert.That(
        () => ColorReductionDispatch.ReduceByName(_Sample(), "NonExistentQuantizer", "NoDithering_Instance", 16),
        Throws.ArgumentException);
      Assert.That(
        () => ColorReductionDispatch.ReduceByName(_Sample(), "Wu", "NonExistentDitherer", 16),
        Throws.ArgumentException);
    });
  }

  private static RawImage _Sample() {
    const int width = 32, height = 16;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 255 / (width - 1));
      rgb[at + 1] = (byte)(y * 255 / (height - 1));
      rgb[at + 2] = (byte)((x * y) & 255);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
