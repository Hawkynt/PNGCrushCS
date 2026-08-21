using System;
using System.Linq;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;

namespace Hawkynt.ColorProcessing.Adapter.Tests;

/// <summary>
/// Selecting a quantizer and a ditherer by name, through the generated dispatch.
/// </summary>
/// <remarks>
/// What matters here is not that any particular algorithm is good but that the generated table
/// describes what is really there: every name it offers can be used, a name it does not offer is
/// refused rather than silently doing something else, and nothing had to be reflected to find out.
/// </remarks>
[TestFixture]
public sealed class ColorReductionDispatchTests {

  [Test]
  [Category("Unit")]
  public void TheDispatchOffersWhatTheLibraryHas() {
    Assert.Multiple(() => {
      Assert.That(ColorReductionDispatch.QuantizerNames, Is.Not.Empty);
      Assert.That(ColorReductionDispatch.DithererNames, Is.Not.Empty);

      // Names have to be distinct or one of them can never be chosen.
      Assert.That(
        ColorReductionDispatch.QuantizerNames.Distinct().Count(),
        Is.EqualTo(ColorReductionDispatch.QuantizerNames.Length));
      Assert.That(
        ColorReductionDispatch.DithererNames.Distinct().Count(),
        Is.EqualTo(ColorReductionDispatch.DithererNames.Length));

      // The ones the optimizers name by hand have to still be there.
      Assert.That(ColorReductionDispatch.QuantizerNames, Does.Contain("WuQuantizer"));
      Assert.That(ColorReductionDispatch.QuantizerNames, Does.Contain("OctreeQuantizer"));
      Assert.That(ColorReductionDispatch.DithererNames, Does.Contain("ErrorDiffusion.FloydSteinberg"));

      // Even a ditherer that does nothing publishes its configuration as a preset, so it is named
      // the same way as the rest rather than by its bare type.
      Assert.That(ColorReductionDispatch.DithererNames, Does.Contain("NoDithering.Instance"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnUnknownNameIsRefused() {
    var image = _Gradient(4, 4);

    Assert.Multiple(() => {
      Assert.That(
        () => ColorReductionDispatch.Reduce(image, "NoSuchQuantizer", "ErrorDiffusion.FloydSteinberg", 4),
        Throws.ArgumentException);
      Assert.That(
        () => ColorReductionDispatch.Reduce(image, "WuQuantizer", "NoSuchDitherer", 4),
        Throws.ArgumentException);
    });
  }

  /// <summary>
  /// Every offered pairing has to run. This is the check that a generated table cannot drift from
  /// the library it was generated against — a name that no longer builds is a compile error, and a
  /// name that builds but throws is caught here.
  /// </summary>
  /// <remarks>
  /// Not run by default, and the picture is four pixels square. Several of these are iterative —
  /// genetic, neural, direct binary search — and their cost is in their iteration count rather
  /// than in the input, so a sweep of all of them does not finish in any time worth waiting for
  /// however small the picture is. That is a fact about the algorithms rather than a defect, but
  /// it does mean the sweep belongs behind a switch: run it when the library is upgraded, not on
  /// every build.
  /// </remarks>
  [Test]
  [Explicit("Sweeps every algorithm the library offers; several are iterative and take minutes.")]
  [Category("Exhaustive")]
  public void EveryQuantizerRuns() {
    var image = _Gradient(4, 4);
    var failures = ColorReductionDispatch.QuantizerNames
      .Select(name => {
        try {
          var reduced = ColorReductionDispatch.Reduce(image, name, "ErrorDiffusion.FloydSteinberg", 4);
          return reduced.PaletteCount == 4 && reduced.PixelData.All(i => i < 4)
            ? null
            : $"{name}: produced {reduced.PaletteCount} colours";
        } catch (Exception ex) {
          return $"{name}: {ex.GetType().Name}: {ex.Message}";
        }
      })
      .Where(f => f != null)
      .ToList();

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }

  /// <inheritdoc cref="EveryQuantizerRuns"/>
  [Test]
  [Explicit("Sweeps every configuration the library offers; several are iterative and take minutes.")]
  [Category("Exhaustive")]
  public void EveryDithererRuns() {
    var image = _Gradient(4, 4);
    var failures = ColorReductionDispatch.DithererNames
      .Select(name => {
        try {
          var reduced = ColorReductionDispatch.Reduce(image, "WuQuantizer", name, 4);
          return reduced.PixelData.All(i => i < 4) ? null : $"{name}: used an index past the palette";
        } catch (Exception ex) {
          return $"{name}: {ex.GetType().Name}: {ex.Message}";
        }
      })
      .Where(f => f != null)
      .ToList();

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }

  private static RawImage _Gradient(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 255 / (width - 1));
      rgb[at + 1] = (byte)(y * 255 / (height - 1));
      rgb[at + 2] = (byte)((x + y) * 255 / (width + height - 2));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// The pairings the optimizers actually name, which have to work on every build.
  /// </summary>
  /// <remarks>
  /// These are the ones the CLI offers and the README promises, so a regression in any of them is
  /// a regression a user would meet. The rest are covered by the sweeps above when the library
  /// moves.
  /// </remarks>
  [TestCase("WuQuantizer", "ErrorDiffusion.FloydSteinberg")]
  [TestCase("WuQuantizer", "NoDithering.Instance")]
  [TestCase("OctreeQuantizer", "ErrorDiffusion.FloydSteinberg")]
  [TestCase("OctreeQuantizer", "ErrorDiffusion.Atkinson")]
  [TestCase("MedianCutQuantizer", "OrderedDitherer.Bayer4x4")]
  [TestCase("UniformQuantizer", "ErrorDiffusion.Sierra")]
  [Category("Unit")]
  public void TheNamedPairings_Run(string quantizer, string ditherer) {
    var image = _Gradient(32, 16);

    var reduced = ColorReductionDispatch.Reduce(image, quantizer, ditherer, 8);

    Assert.Multiple(() => {
      Assert.That(reduced.Width, Is.EqualTo(32));
      Assert.That(reduced.Height, Is.EqualTo(16));
      Assert.That(reduced.PaletteCount, Is.EqualTo(8));
      Assert.That(reduced.PixelData.All(i => i < 8), Is.True, "used an index past the palette");
      Assert.That(reduced.PixelData.Distinct().Count(), Is.GreaterThan(1), "came out flat");
    });
  }

  /// <summary>
  /// The underscore-joined spellings Optimizer.Png hands through, resolved to the generated name
  /// they actually mean.
  /// </summary>
  /// <remarks>
  /// Optimizer.Png's own default ditherer list is written in this shorthand, so a name here that
  /// stops resolving is a name Optimizer.Png stops being able to select, silently, until something
  /// runs it. Checking the resolution here rather than only through a round trip is what makes a
  /// typo in the shorthand a build-time-adjacent failure instead of one that only ever shows up on
  /// whichever platform happens to exercise that particular ditherer.
  /// </remarks>
  [TestCase("NoDithering_Instance", "NoDithering.Instance")]
  [TestCase("ErrorDiffusion_FloydSteinberg", "ErrorDiffusion.FloydSteinberg")]
  [TestCase("ErrorDiffusion_Atkinson", "ErrorDiffusion.Atkinson")]
  [TestCase("ErrorDiffusion_Sierra", "ErrorDiffusion.Sierra")]
  [TestCase("OrderedDitherer_Bayer4x4", "OrderedDitherer.Bayer4x4")]
  [Category("Unit")]
  public void TheUnderscoredShorthand_ResolvesToTheNameItMeans(string shorthand, string expected) {
    Assert.That(ColorReductionDispatch.ResolveDitherer(shorthand), Is.EqualTo(expected));
  }
}
