using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The colour reduction the optimizers select by name, on <see cref="RawImage"/> rather than on a
/// platform bitmap.
/// </summary>
/// <remarks>
/// Quantizing is a judgement and a test cannot say a palette is good. What it can say is that the
/// promises hold: a picture already within the palette comes back untouched, a ditherer that
/// claims to reach two rows does not write into a third, and every name the registry offers can
/// actually be used. Those are the things that break silently.
/// </remarks>
[TestFixture]
public sealed class ColorReductionTests {

  [Test]
  [Category("Unit")]
  public void EveryOfferedName_CanBeFound() {
    Assert.Multiple(() => {
      foreach (var quantizer in ColorReduction.Quantizers)
        Assert.That(ColorReduction.FindQuantizer(quantizer.Name), Is.SameAs(quantizer), quantizer.Name);

      foreach (var ditherer in ColorReduction.Ditherers)
        Assert.That(ColorReduction.FindDitherer(ditherer.Name), Is.SameAs(ditherer), ditherer.Name);
    });

    Assert.Multiple(() => {
      Assert.That(ColorReduction.FindQuantizer("nosuchthing"), Is.Null);
      Assert.That(ColorReduction.FindDitherer("nosuchthing"), Is.Null);
    });
  }

  /// <summary>
  /// A picture that already fits leaves nothing for the reduction to decide, so every pairing has
  /// to return it unchanged — including the dithering ones, whose error is nought throughout.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void PictureAlreadyWithinThePalette_SurvivesEveryPairing() {
    var source = _Sample(12);
    var failures = new List<string>();

    foreach (var quantizer in ColorReduction.Quantizers) {
      // The uniform quantizer does not look at the picture, so it cannot be expected to contain it.
      if (quantizer is UniformQuantizer)
        continue;

      foreach (var ditherer in ColorReduction.Ditherers) {
        var reduced = ColorReduction.Reduce(source, quantizer, ditherer, 16);
        var actual = PixelConverter.Convert(reduced, PixelFormat.Rgb24);

        for (var i = 0; i < source.PixelData.Length; ++i) {
          if (source.PixelData[i] == actual.PixelData[i])
            continue;

          failures.Add(
            $"{quantizer.Name}+{ditherer.Name}: byte {i} went in as {source.PixelData[i]} "
            + $"and came back as {actual.PixelData[i]}");

          break;
        }
      }
    }

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }

  /// <summary>
  /// A picture with more colours than the palette still has to come out the right shape, whichever
  /// pairing is used, and to use only colours the palette holds.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void PictureBeyondThePalette_ReducesToItAndNoFurther() {
    var source = _Sample(200);
    var failures = new List<string>();

    foreach (var quantizer in ColorReduction.Quantizers)
    foreach (var ditherer in ColorReduction.Ditherers) {
      var reduced = ColorReduction.Reduce(source, quantizer, ditherer, 16);

      if (reduced.Width != source.Width || reduced.Height != source.Height) {
        failures.Add($"{quantizer.Name}+{ditherer.Name}: {reduced.Width}x{reduced.Height}");
        continue;
      }

      var used = reduced.PixelData.Distinct().ToList();
      if (used.Any(index => index >= 16))
        failures.Add($"{quantizer.Name}+{ditherer.Name}: used an index past the palette");
    }

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }

  private static RawImage _Sample(int colors) {
    const int width = 40, height = 24;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var index = (x + y * width) % colors;

      rgb[at] = (byte)(index * 251 % 256);
      rgb[at + 1] = (byte)(index * 149 % 256);
      rgb[at + 2] = (byte)(index * 97 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
