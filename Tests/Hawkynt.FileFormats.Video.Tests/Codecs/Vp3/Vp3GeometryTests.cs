using System.Linq;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>
/// The block, super block and macro block orders, against the worked example the specification prints.
/// </summary>
/// <remarks>
/// Sections 2.3 and 2.4 of the Theora specification take a 240&#215;48 frame and print the coded-order
/// index of every block and every macro block in the corners of it. That example is the test: it
/// covers the Hilbert curve inside a full super block, the same curve inside a super block that hangs
/// off the top of the frame with only half its rows present, and the numbering carrying on from one
/// super block row to the next without restarting.
/// <para/>
/// Getting this wrong is not subtle in its effect but is very subtle in its cause — every flag, token
/// and motion vector in a VP3 frame is in coded order, so a curve that is right for fifteen of the
/// sixteen blocks decodes a frame whose contents are scattered rather than one that fails.
/// </remarks>
[TestFixture]
public sealed class Vp3GeometryTests {

  /// <summary>The specification's worked example: 240&#215;48, so fifteen macro blocks by three.</summary>
  private static Vp3Geometry _Example() => new(15, 3);

  [Test]
  [Category("Unit")]
  public void TheExampleFrameHasTheCountsTheSpecificationStates() {
    var geometry = _Example();

    // Thirty blocks across and six rows in the luma plane, half that each way in the chroma planes.
    Assert.That(geometry.PlaneBlockWidth, Is.EqualTo(new[] { 30, 15, 15 }));
    Assert.That(geometry.PlaneBlockHeight, Is.EqualTo(new[] { 6, 3, 3 }));
    Assert.That(geometry.PlaneWidth, Is.EqualTo(new[] { 240, 120, 120 }));
    Assert.That(geometry.PlaneHeight, Is.EqualTo(new[] { 48, 24, 24 }));

    // Six blocks per macro block in 4:2:0, and four of them luma.
    Assert.That(geometry.BlockCount, Is.EqualTo(6 * 15 * 3));
    Assert.That(geometry.LumaBlockCount, Is.EqualTo(4 * 15 * 3));
    Assert.That(geometry.MacroblockCount, Is.EqualTo(45));

    // Eight super block columns by two rows in the luma plane, four by one in each chroma plane.
    Assert.That(geometry.SuperBlockCount, Is.EqualTo(8 * 2 + 4 * 1 + 4 * 1));
  }

  [Test]
  [Category("Unit")]
  public void TheFirstSuperBlockIsWalkedAlongTheHilbertCurveOfFigureTwoFour() {
    // Rows are printed bottom-up, which is the coordinate system, so this is Figure 2.4 as it stands.
    var expected = new[] {
      new[] { 0, 1, 14, 15 },
      new[] { 3, 2, 13, 12 },
      new[] { 4, 7, 8, 11 },
      new[] { 5, 6, 9, 10 },
    };

    var geometry = _Example();
    var index = geometry.CodedIndex[0];

    for (var row = 0; row < 4; ++row)
    for (var column = 0; column < 4; ++column)
      Assert.That(index[row * 30 + column], Is.EqualTo(expected[row][column]),
        $"block at column {column}, row {row}");
  }

  [Test]
  [Category("Unit")]
  public void ASuperBlockHangingOffTheTopOfTheFrameKeepsTheOrderAndSkipsWhatIsNotThere() {
    // The second super block row of the example holds only two of its four block rows. The
    // specification prints its first super block as 120, 121 across the lower row and 123, 122 across
    // the upper — the curve's fifth to twelfth steps fall outside the frame and are simply left out,
    // so the numbering jumps from 123 to 124 at the far side.
    var geometry = _Example();
    var index = geometry.CodedIndex[0];

    Assert.That(index[4 * 30 + 0], Is.EqualTo(120));
    Assert.That(index[4 * 30 + 1], Is.EqualTo(121));
    Assert.That(index[5 * 30 + 1], Is.EqualTo(122));
    Assert.That(index[5 * 30 + 0], Is.EqualTo(123));
    Assert.That(index[5 * 30 + 3], Is.EqualTo(124));
    Assert.That(index[5 * 30 + 2], Is.EqualTo(125));
    Assert.That(index[4 * 30 + 2], Is.EqualTo(126));
    Assert.That(index[4 * 30 + 3], Is.EqualTo(127));
  }

  [Test]
  [Category("Unit")]
  public void BlockIndicesRunOnFromOnePlaneToTheNextWithoutRestarting() {
    // The whole frame's blocks are one sequence, because the coded block flags and the DCT tokens are
    // read for all three planes in one pass.
    var geometry = _Example();

    Assert.That(geometry.CodedIndex[0].Min(), Is.Zero);
    Assert.That(geometry.CodedIndex[0].Max(), Is.EqualTo(geometry.LumaBlockCount - 1));
    Assert.That(geometry.CodedIndex[1].Min(), Is.EqualTo(geometry.LumaBlockCount));
    Assert.That(geometry.CodedIndex[2].Max(), Is.EqualTo(geometry.BlockCount - 1));

    // Every block index is used exactly once across the three planes.
    var all = geometry.CodedIndex.SelectMany(plane => plane).OrderBy(block => block);
    Assert.That(all, Is.EqualTo(Enumerable.Range(0, geometry.BlockCount)));
  }

  [Test]
  [Category("Unit")]
  public void MacroBlocksAreWalkedAlongTheSmallerCurveOfFigureTwoSix() {
    // The indices Section 2.4 prints for the same frame: fifteen macro blocks across, three rows.
    var geometry = _Example();

    var bottom = new[] { 0, 3, 4, 7 };
    var middle = new[] { 1, 2, 5, 6 };
    for (var column = 0; column < 4; ++column) {
      Assert.That(_Macroblock(geometry, column, 0), Is.EqualTo(bottom[column]), $"column {column}, row 0");
      Assert.That(_Macroblock(geometry, column, 1), Is.EqualTo(middle[column]), $"column {column}, row 1");
    }

    // The far end of the first two rows, where the last super block holds one column rather than two.
    Assert.That(_Macroblock(geometry, 12, 0), Is.EqualTo(24));
    Assert.That(_Macroblock(geometry, 13, 0), Is.EqualTo(27));
    Assert.That(_Macroblock(geometry, 14, 0), Is.EqualTo(28));
    Assert.That(_Macroblock(geometry, 12, 1), Is.EqualTo(25));
    Assert.That(_Macroblock(geometry, 13, 1), Is.EqualTo(26));
    Assert.That(_Macroblock(geometry, 14, 1), Is.EqualTo(29));

    // The top row, whose super blocks hold one macro block row rather than two.
    Assert.That(_Macroblock(geometry, 0, 2), Is.EqualTo(30));
    Assert.That(_Macroblock(geometry, 1, 2), Is.EqualTo(31));
    Assert.That(_Macroblock(geometry, 2, 2), Is.EqualTo(32));
    Assert.That(_Macroblock(geometry, 3, 2), Is.EqualTo(33));
    Assert.That(_Macroblock(geometry, 12, 2), Is.EqualTo(42));
    Assert.That(_Macroblock(geometry, 13, 2), Is.EqualTo(43));
    Assert.That(_Macroblock(geometry, 14, 2), Is.EqualTo(44));
  }

  [Test]
  [Category("Unit")]
  public void EachMacroBlockOwnsFourLumaBlocksInRasterOrderAndOneBlockOfEachChromaPlane() {
    var geometry = _Example();

    for (var macroblock = 0; macroblock < geometry.MacroblockCount; ++macroblock) {
      var luma = geometry.MacroblockLumaBlocks[macroblock];
      var chroma = geometry.MacroblockChromaBlocks[macroblock];

      Assert.That(luma.Length, Is.EqualTo(4));
      Assert.That(chroma.Length, Is.EqualTo(2));

      // Lower left, lower right, upper left, upper right — the order 7.5.2 reads four vectors in.
      Assert.That(geometry.BlockRow[luma[0]], Is.EqualTo(geometry.BlockRow[luma[1]]));
      Assert.That(geometry.BlockColumn[luma[1]], Is.EqualTo(geometry.BlockColumn[luma[0]] + 1));
      Assert.That(geometry.BlockRow[luma[2]], Is.EqualTo(geometry.BlockRow[luma[0]] + 1));
      Assert.That(geometry.BlockColumn[luma[2]], Is.EqualTo(geometry.BlockColumn[luma[0]]));
      Assert.That(geometry.BlockRow[luma[3]], Is.EqualTo(geometry.BlockRow[luma[0]] + 1));
      Assert.That(geometry.BlockColumn[luma[3]], Is.EqualTo(geometry.BlockColumn[luma[0]] + 1));

      Assert.That(geometry.BlockPlane[chroma[0]], Is.EqualTo(1));
      Assert.That(geometry.BlockPlane[chroma[1]], Is.EqualTo(2));

      foreach (var block in luma)
        Assert.That(geometry.MacroblockOfBlock[block], Is.EqualTo(macroblock));

      foreach (var block in chroma)
        Assert.That(geometry.MacroblockOfBlock[block], Is.EqualTo(macroblock));
    }
  }

  [Test]
  [Category("Unit")]
  public void EveryBlockOfASuperBlockNamesTheSameSuperBlock() {
    // The coded block flags are stated per super block first and only then per block, so a block that
    // named the wrong one would take its flag from somewhere else in the frame.
    var geometry = new Vp3Geometry(7, 5);

    for (var block = 0; block < geometry.BlockCount; ++block) {
      var plane = geometry.BlockPlane[block];
      var expected = geometry.BlockColumn[block] / 4
        + geometry.BlockRow[block] / 4 * ((geometry.PlaneBlockWidth[plane] + 3) / 4);

      var offset = 0;
      for (var earlier = 0; earlier < plane; ++earlier)
        offset += (geometry.PlaneBlockWidth[earlier] + 3) / 4 * ((geometry.PlaneBlockHeight[earlier] + 3) / 4);

      Assert.That(geometry.BlockSuperBlock[block], Is.EqualTo(offset + expected), $"block {block}");
    }
  }

  private static int _Macroblock(Vp3Geometry geometry, int column, int row) {
    // Recovered from the luma blocks, which is the only way in from outside.
    for (var macroblock = 0; macroblock < geometry.MacroblockCount; ++macroblock) {
      var first = geometry.MacroblockLumaBlocks[macroblock][0];
      if (geometry.BlockColumn[first] == column * 2 && geometry.BlockRow[first] == row * 2)
        return macroblock;
    }

    return -1;
  }
}
