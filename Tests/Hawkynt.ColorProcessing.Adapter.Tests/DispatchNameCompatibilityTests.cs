using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;

namespace Hawkynt.ColorProcessing.Adapter.Tests;

/// <summary>
/// The names callers already use have to keep working.
/// </summary>
/// <remarks>
/// The generated names are the types' own, which is what can be checked at build time; the names
/// on the command line predate the generator. Breaking them would be a regression a user meets
/// immediately, so the old spellings are translated — and a name that means nothing is still
/// refused, which is what having a generated list is for.
/// </remarks>
[TestFixture]
public sealed class DispatchNameCompatibilityTests {

  [TestCase("Wu", "WuQuantizer")]
  [TestCase("Octree", "OctreeQuantizer")]
  [TestCase("Median Cut", "MedianCutQuantizer")]
  [TestCase("WuQuantizer", "WuQuantizer")]
  [Category("Unit")]
  public void OldQuantizerNames_StillResolve(string given, string expected)
    => Assert.That(ColorReductionDispatch.ResolveQuantizer(given), Is.EqualTo(expected));

  [TestCase("ErrorDiffusion_FloydSteinberg", "ErrorDiffusion.FloydSteinberg")]
  [TestCase("NoDithering_Instance", "NoDithering.Instance")]
  [TestCase("ErrorDiffusion.Atkinson", "ErrorDiffusion.Atkinson")]
  [Category("Unit")]
  public void OldDithererNames_StillResolve(string given, string expected)
    => Assert.That(ColorReductionDispatch.ResolveDitherer(given), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void ANameThatMeansNothingIsStillRefused() {
    Assert.Multiple(() => {
      Assert.That(ColorReductionDispatch.ResolveQuantizer("NoSuchThing"), Is.Null);
      Assert.That(ColorReductionDispatch.ResolveDitherer("NoSuchThing"), Is.Null);
      Assert.That(ColorReductionDispatch.ResolveQuantizer(""), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void ReducingByAnOldNameSaysWhichItUsed() {
    var image = _Gradient(16, 8);

    var result = ColorReductionDispatch.ReduceByName(image, "Wu", "ErrorDiffusion_FloydSteinberg", 4);

    Assert.Multiple(() => {
      Assert.That(result.Quantizer, Is.EqualTo("WuQuantizer"));
      Assert.That(result.Ditherer, Is.EqualTo("ErrorDiffusion.FloydSteinberg"));
      Assert.That(result.Image.PaletteCount, Is.EqualTo(4));
    });
  }

  private static RawImage _Gradient(int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 255 / (width - 1));
      rgb[at + 1] = (byte)(y * 255 / (height - 1));
      rgb[at + 2] = 128;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
