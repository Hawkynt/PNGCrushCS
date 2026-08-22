using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;

namespace Hawkynt.ColorProcessing.Adapter.Tests;

/// <summary>
/// A quantizer that carries settings has to be built with its constructor, not zeroed.
/// </summary>
/// <remarks>
/// The dispatch used to emit <c>default(T)</c>, which skips the struct's parameterless constructor
/// and so its property initialisers. <c>PngQuantQuantizer</c> then ran with
/// <c>MedianCutIterations</c> at 0, never entered the loop that fills its palette, and threw a
/// <see cref="NullReferenceException"/> in the next stage. Nothing in the name resolution above
/// could see that, so it is checked by running one.
/// </remarks>
[TestFixture]
public sealed class ConfiguredQuantizerTests {

  [TestCase("PngQuant")]
  [TestCase("Neuquant")]
  [TestCase("Wu")]
  [TestCase("Octree")]
  [TestCase("Median Cut")]
  [Category("Unit")]
  public void ReducesWithoutThrowing(string quantizer) {
    var result = ColorReductionDispatch.ReduceByName(_Colorful(32, 32), quantizer, "NoDithering_Instance", 16);

    Assert.That(result.Image.PaletteCount, Is.GreaterThan(0));
  }

  private static RawImage _Colorful(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 8 % 256);
      rgb[at + 1] = (byte)(y * 8 % 256);
      rgb[at + 2] = (byte)((x + y) * 4 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
