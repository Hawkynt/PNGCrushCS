using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>The plane and palette conversions every Atari ST format is built on.</summary>
[TestFixture]
public sealed class PlanarConverterTests {

  /// <summary>Every colour the hardware can show has to survive being written and read back.</summary>
  /// <remarks>
  /// This is the check no decoder-against-decoder comparison can make. A writer that narrows a
  /// channel wrongly stores a palette that both our reader and any other reader then decode the
  /// same way, so the two agree perfectly on a picture that is not the one that went in — the
  /// disagreement is with the source, and only the source can show it.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void EveryStColor_SurvivesBeingNarrowedAndWidenedAgain() {
    for (var red = 0; red < 8; ++red)
    for (var green = 0; green < 8; ++green)
    for (var blue = 0; blue < 8; ++blue) {
      var expected = (short)((red << 8) | (green << 4) | blue);

      var rgb = PlanarConverter.StPaletteToRgb([expected]);
      var actual = PlanarConverter.RgbToStPalette(rgb, 1)[0];

      if (actual == expected)
        continue;

      Assert.Fail(
        $"0x{expected:X3} widened to ({rgb[0]}, {rgb[1]}, {rgb[2]}) and came back as 0x{actual:X3}");
    }
  }
}
