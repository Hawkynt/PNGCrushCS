using System;
using System.IO;
using FileFormat.JpegXl;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Where each transform starts, and why it has to be carried rather than
/// worked out.
/// </summary>
/// <remarks>
/// A transform larger than one block covers a rectangle of cells, and only the
/// cell it starts at carries coefficients. The format states that per cell —
/// libjxl keeps it as a bit beside the transform's own number. This decoder
/// used to throw the bit away and work the answer out again from the shapes of
/// the neighbouring cells, which cannot tell two transforms of the same shape
/// side by side apart from one transform twice the width. Most of a picture
/// coded in large transforms is exactly that, and every cell read as covered
/// took no coefficients where the file had put some.
/// </remarks>
[TestFixture]
internal sealed class JxlTransformOriginTests {

  /// <summary>Two 16x16 transforms side by side, which is four cells in a row
  /// all naming the same shape.</summary>
  [Test]
  public void TheShapesOfTheNeighbouringCellsCannotSayWhereTheSecondTransformStarts() {
    var plane = new JxlAcStrategyType[2][];
    for (var y = 0; y < 2; ++y) {
      plane[y] = new JxlAcStrategyType[4];
      for (var x = 0; x < 4; ++x)
        plane[y][x] = JxlAcStrategyType.Dct16x16;
    }

    Assert.Multiple(() => {
      Assert.That(JxlAcStrategyGeometry.IsTransformOrigin(plane, 0, 0), Is.True, "the first one does start here");
      Assert.That(JxlAcStrategyGeometry.IsTransformOrigin(plane, 2, 0), Is.False,
        "and the second one does too, but working it out from the neighbours says otherwise");
    });
  }

  /// <summary>
  /// A 96x96 file cjxl 0.12.0 wrote. Its blocks are covered by transforms
  /// larger than one block sitting next to others of the same shape, so the
  /// worked-out answer is wrong for a good number of them, and the coefficient
  /// stream stops making sense partway through the first channel.
  /// </summary>
  [Test]
  public void APictureOfAdjacentTransformsOfTheSameShapeDecodes() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "cjxl_adjacent_transforms.jxl");
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");

    var decoded = JpegXlReader.TryReadSpecRgb24(File.ReadAllBytes(path), out var width, out var height, out var rgb);

    Assert.Multiple(() => {
      Assert.That(decoded, Is.True, "the coefficient stream has to run to the end of every block");
      Assert.That(width, Is.EqualTo(96));
      Assert.That(height, Is.EqualTo(96));
      Assert.That(rgb, Is.Not.Null.And.Length.EqualTo(96 * 96 * 3));
    });
  }
}
