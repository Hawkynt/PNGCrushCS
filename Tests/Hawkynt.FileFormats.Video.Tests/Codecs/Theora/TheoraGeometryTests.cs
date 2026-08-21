using System.Linq;

namespace FileFormat.Codecs.Theora.Tests;

/// <summary>
/// The block, super block and macro block layout, against the worked example in the specification.
/// </summary>
/// <remarks>
/// Section 2.3 of the Theora specification writes out the coded-order index of every block of a
/// 240x48 frame, and section 2.4 does the same for its macro blocks. Those two tables are the whole
/// of what is asserted here, because getting them wrong is the failure that is hardest to see: a
/// decoder with the ordering subtly wrong still produces a picture, and the picture is scrambled in
/// a way that looks like a bug somewhere else entirely.
/// </remarks>
[TestFixture]
public sealed class TheoraGeometryTests {

  /// <summary>The 240x48 frame the specification works through: fifteen macro blocks by three.</summary>
  private static TheoraGeometry _Example()
    => new(new() {
      VersionMajor = 3,
      VersionMinor = 2,
      VersionRevision = 1,
      FrameMacroBlocksWide = 15,
      FrameMacroBlocksHigh = 3,
      PictureWidth = 240,
      PictureHeight = 48,
      PictureX = 0,
      PictureY = 0,
      FrameRateNumerator = 25,
      FrameRateDenominator = 1,
      AspectNumerator = 1,
      AspectDenominator = 1,
      ColorSpace = 0,
      NominalBitrate = 0,
      Quality = 0,
      KeyFrameGranuleShift = 6,
      PixelFormat = TheoraPixelFormat.Yuv420,
    });

  [Test]
  [Category("Unit")]
  public void BlocksAreWalkedAlongTheHilbertCurveInsideEachSuperBlock() {
    // Section 2.3, read out with row zero at the bottom: super blocks in raster order, and within
    // each of them the blocks along the curve of Figure 2.4. The luma plane of this frame is thirty
    // blocks across and six high, which is eight super blocks by two — the last of each row of super
    // blocks holding only two of its four columns of blocks.
    var geometry = _Example();

    Assert.That(geometry.PlaneBlocksWide[0], Is.EqualTo(30));
    Assert.That(geometry.PlaneBlocksHigh[0], Is.EqualTo(6));

    int[][] expected = [
      [0, 1, 14, 15],
      [3, 2, 13, 12],
      [4, 7, 8, 11],
      [5, 6, 9, 10],
      [120, 121, 126, 127],
      [123, 122, 125, 124],
    ];

    for (var row = 0; row < expected.Length; ++row)
    for (var column = 0; column < expected[row].Length; ++column)
      Assert.That(geometry.BlockAt(0, column, row), Is.EqualTo(expected[row][column]),
        $"block at column {column}, row {row}");

    // And the far right of the frame, where the specification's table gives the last two columns —
    // note that the top row runs 179 then 178, because the curve doubles back before it runs out of
    // super block to walk.
    Assert.That(geometry.BlockAt(0, 28, 0), Is.EqualTo(112));
    Assert.That(geometry.BlockAt(0, 29, 0), Is.EqualTo(113));
    Assert.That(geometry.BlockAt(0, 28, 4), Is.EqualTo(176));
    Assert.That(geometry.BlockAt(0, 29, 4), Is.EqualTo(177));
    Assert.That(geometry.BlockAt(0, 28, 5), Is.EqualTo(179));
    Assert.That(geometry.BlockAt(0, 29, 5), Is.EqualTo(178));
  }

  [Test]
  [Category("Unit")]
  public void MacroBlocksAreWalkedAlongTheirOwnSmallerCurve() {
    // Section 2.4: fifteen macro blocks in each row and three rows, with the four inside each luma
    // super block taken along the curve of Figure 2.6. The top row of super blocks holds only one of
    // its two rows of macro blocks, and the ones that fall outside are simply left out — which is
    // why the top row of macro blocks is numbered 30 onwards rather than interleaved.
    var geometry = _Example();

    Assert.That(geometry.MacroBlockCount, Is.EqualTo(45));

    int[][] expected = [
      [0, 3, 4, 7],
      [1, 2, 5, 6],
      [30, 31, 32, 33],
    ];

    for (var row = 0; row < expected.Length; ++row)
    for (var column = 0; column < expected[row].Length; ++column)
      Assert.That(geometry.MacroBlockAt[row * 15 + column], Is.EqualTo(expected[row][column]),
        $"macro block at column {column}, row {row}");

    Assert.That(geometry.MacroBlockAt[14], Is.EqualTo(28));
    Assert.That(geometry.MacroBlockAt[15 + 14], Is.EqualTo(29));
    Assert.That(geometry.MacroBlockAt[30 + 14], Is.EqualTo(44));
  }

  [Test]
  [Category("Unit")]
  public void TheCountsAreTheOnesTheIdentificationHeaderImplies() {
    // Tables 6.5 and 6.6: for 4:2:0, six blocks a macro block, and a super block count that is the
    // luma plane's plus one for each chroma plane — each of those being a quarter the size, and each
    // rounded up separately.
    var geometry = _Example();

    Assert.That(geometry.BlockCount, Is.EqualTo(6 * 15 * 3));
    Assert.That(geometry.LumaBlockCount, Is.EqualTo(4 * 15 * 3));
    Assert.That(geometry.SuperBlockCount, Is.EqualTo(8 * 2 + 2 * (4 * 1)));

    // Every block is reached exactly once by the coded order, which is what makes the raster and
    // coded orderings two views of the same set rather than two different sets.
    Assert.That(geometry.RasterToCoded.Distinct().Count(), Is.EqualTo(geometry.BlockCount));
  }

  [Test]
  [Category("Unit")]
  public void ASuperBlockAtAnEdgeHoldsFewerThanSixteenBlocks() {
    // The count matters: the coded block flags read one bit for each block a partially coded super
    // block actually holds, and a reader assuming sixteen would take the wrong number of them and
    // lose its place for the rest of the frame.
    var geometry = _Example();

    var whole = 0;
    var partial = 0;
    foreach (var count in geometry.SuperBlockBlockCount)
      if (count == 16)
        ++whole;
      else
        ++partial;

    Assert.That(geometry.SuperBlockBlockCount.Sum(), Is.EqualTo(geometry.BlockCount));
    Assert.That(partial, Is.GreaterThan(0), "a 30-by-6 plane cannot be covered by whole super blocks");
    Assert.That(whole + partial, Is.EqualTo(geometry.SuperBlockCount));
  }
}
