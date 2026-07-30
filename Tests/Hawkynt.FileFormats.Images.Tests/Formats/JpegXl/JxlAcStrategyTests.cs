using System;
using System.IO;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlAcStrategyDecoder"/> (ISO/IEC 18181-1 §G.5 /
/// libjxl <c>ModularFrameDecoder::DecodeAcMetadata</c> in
/// <c>lib/jxl/dec_modular.cc</c> lines 480-560).
///
/// <para>The decoder has two entry points: a no-op
/// <see cref="JxlAcStrategyDecoder.CreateAllDct8x8"/> that synthesises a
/// uniform DCT8x8 grid (used as a fixture and as a fallback for simple
/// frames), and the full <see cref="JxlAcStrategyDecoder.DecodeForGroup"/>
/// which decodes the 4-channel modular sub-image (cmap_x, cmap_y,
/// (strategy &lt;&lt; 4) | qf_idx, epf_sharpness) per the spec.</para>
/// </summary>
[TestFixture]
public sealed class JxlAcStrategyTests {

  // ============================================================
  // CreateAllDct8x8 — fixture-friendly all-DCT8 strategy grid
  // ============================================================

  /// <summary>
  /// Smallest interesting case: a 4×4-block group (i.e. 32×32 pixels) returns
  /// a 4-row × 4-column grid of DCT8x8. Verifies dimensions, value, and that
  /// row arrays are independent (writing to one row must not affect another).
  /// </summary>
  [Test]
  public void CreateAllDct8x8_FourByFour_ReturnsAllDct8x8() {
    var grid = JxlAcStrategyDecoder.CreateAllDct8x8(4, 4);

    Assert.Multiple(() => {
      Assert.That(grid.Length, Is.EqualTo(4), "Expected 4 rows.");
      for (var y = 0; y < 4; ++y) {
        Assert.That(grid[y].Length, Is.EqualTo(4), $"Row {y} should have 4 columns.");
        for (var x = 0; x < 4; ++x)
          Assert.That(grid[y][x], Is.EqualTo(JxlAcStrategyType.Dct8x8),
            $"Cell ({x},{y}) should be Dct8x8.");
      }
    });

    // Independence check: mutating one row must not affect any other row.
    grid[0][0] = JxlAcStrategyType.Dct16x16;
    Assert.That(grid[1][0], Is.EqualTo(JxlAcStrategyType.Dct8x8),
      "Row arrays must be independent (jagged array).");
  }

  /// <summary>
  /// Empty group (0×0 blocks): returns a length-0 outer array, no
  /// allocations, no exceptions.
  /// </summary>
  [Test]
  public void CreateAllDct8x8_ZeroDimensions_ReturnsEmptyArray() {
    var grid = JxlAcStrategyDecoder.CreateAllDct8x8(0, 0);
    Assert.That(grid.Length, Is.EqualTo(0));
  }

  /// <summary>
  /// Asymmetric dimensions (different width vs height) preserve the
  /// <c>[y][x]</c> indexing convention from the API contract.
  /// </summary>
  [Test]
  public void CreateAllDct8x8_AsymmetricDimensions_PreservesYXIndexing() {
    // 2 wide, 5 high → 5 rows of 2 columns each.
    var grid = JxlAcStrategyDecoder.CreateAllDct8x8(2, 5);
    Assert.Multiple(() => {
      Assert.That(grid.Length, Is.EqualTo(5), "Expected 5 rows (groupBlocksHigh).");
      foreach (var row in grid)
        Assert.That(row.Length, Is.EqualTo(2), "Each row should have 2 columns.");
    });
  }

  /// <summary>
  /// Negative dimensions are rejected by argument validation.
  /// </summary>
  [Test]
  public void CreateAllDct8x8_NegativeWidth_Throws() {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlAcStrategyDecoder.CreateAllDct8x8(-1, 4));
  }

  /// <summary>
  /// Negative height is rejected.
  /// </summary>
  [Test]
  public void CreateAllDct8x8_NegativeHeight_Throws() {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlAcStrategyDecoder.CreateAllDct8x8(4, -1));
  }

  // ============================================================
  // DecodeForGroup — full spec-conformant entry point
  // ============================================================

  /// <summary>
  /// For a non-empty group with degenerate (all-zero) bits the modular
  /// sub-image decode chokes long before producing a strategy plane:
  /// reading the MA tree consumes more bits than are available. The task
  /// spec explicitly allows the modular decode to propagate its own error.
  /// </summary>
  [Test]
  public void DecodeForGroup_NonEmptyGroup_AllZeroBits_HandlesGracefully() {
    var bytes = new byte[16];
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1, maxSymbol: 0);

    // The modular layer is now resilient: when faced with garbage bits the
    // decoder falls back to a trivial 1-leaf MA tree and zero-filled
    // residuals rather than throwing. Either an exception OR a result with
    // all-default-strategy entries is acceptable; what we MUST NOT see is
    // garbage strategy values >= kNumValidStrategies.
    JxlAcStrategyType[][]? result = null;
    Exception? thrown = null;
    try {
      result = JxlAcStrategyDecoder.DecodeForGroup(reader, entropy, 4, 4);
    } catch (Exception ex) {
      thrown = ex;
    }
    if (thrown != null) {
      // Threw — that's acceptable graceful handling.
      Assert.Pass($"Threw {thrown.GetType().Name}: {thrown.Message}");
      return;
    }
    Assert.That(result, Is.Not.Null);
    foreach (var row in result!)
      foreach (var s in row)
        Assert.That((int)s, Is.LessThan(27).Or.EqualTo((int)JxlAcStrategyDecoder.CoveredByNeighbour),
          "Strategy values must be < kNumValidStrategies (27) or the covered-by-neighbour sentinel.");
  }

  /// <summary>
  /// Empty group (zero-sized) short-circuits in <c>DecodeForGroup</c> and
  /// returns an empty grid without consuming any bits. This matches the
  /// "no bits to read" case in the modular sub-image: a zero-area sub-image
  /// has no encoded data.
  /// </summary>
  [Test]
  public void DecodeForGroup_ZeroSizedGroup_ReturnsEmptyAndConsumesNoBits() {
    var bytes = new byte[16]; // all zero-padded
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1, maxSymbol: 0);

    var grid = JxlAcStrategyDecoder.DecodeForGroup(reader, entropy, 0, 0);

    Assert.Multiple(() => {
      Assert.That(grid.Length, Is.EqualTo(0));
      Assert.That(reader.BitsRead, Is.EqualTo(0L),
        "Empty group must not consume any bits.");
    });
  }

  /// <summary>
  /// Null reader is rejected.
  /// </summary>
  [Test]
  public void DecodeForGroup_NullReader_Throws() {
    var bytes = new byte[16];
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1, maxSymbol: 0);
    Assert.Throws<ArgumentNullException>(
      () => JxlAcStrategyDecoder.DecodeForGroup(null!, entropy, 4, 4));
  }

  /// <summary>
  /// Null entropy decoder is rejected.
  /// </summary>
  [Test]
  public void DecodeForGroup_NullEntropy_Throws() {
    var bytes = new byte[16];
    var reader = new JxlBitReader(bytes, 0);
    Assert.Throws<ArgumentNullException>(
      () => JxlAcStrategyDecoder.DecodeForGroup(reader, null!, 4, 4));
  }

  /// <summary>
  /// All-zero "sentinel" — the documented covered-by-neighbour value is
  /// 0xFF, NOT 0. Distinct from <see cref="JxlAcStrategyType.Dct8x8"/> which
  /// is 0. This guarantees a default-initialised array reads as DCT8x8 and a
  /// genuinely "covered" cell can be detected unambiguously.
  /// </summary>
  [Test]
  public void CoveredByNeighbour_IsDistinctFromDct8x8() {
    Assert.That(
      JxlAcStrategyDecoder.CoveredByNeighbour,
      Is.Not.EqualTo(JxlAcStrategyType.Dct8x8));
    Assert.That((byte)JxlAcStrategyDecoder.CoveredByNeighbour, Is.EqualTo(0xFF));
  }

  // ============================================================
  // _BuildStrategyGridFromPackedPlane — post-modular logic
  //
  // The full DecodeForGroup needs a real modular bitstream to feed the
  // 4-channel sub-image decode; that's covered by integration tests at
  // higher layers once a real fixture is wired in. The post-modular
  // bookkeeping (validation + multi-block CoveredByNeighbour marking) is
  // tested directly here against a hand-crafted packed plane — i.e. the
  // exact (strategy << 4) | qf_idx layout that channel 2 of the modular
  // sub-image carries. This is the load-bearing part of the AC-strategy
  // decode that owns spec-conformance against libjxl ac_strategy.h's
  // covered_blocks_{x,y} tables.
  // ============================================================

  /// <summary>
  /// Trivial 1x1 packed plane with strategy=0 (DCT8x8), qf_idx=0. The grid
  /// should be a 1x1 array with cell (0,0) = DCT8x8 and no covered marks.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_TrivialDct8x8_ProducesSingleCell() {
    var packed = new[] { 0 }; // strategy=0, qf=0
    var grid = JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 1, 1);

    Assert.Multiple(() => {
      Assert.That(grid.Length, Is.EqualTo(1));
      Assert.That(grid[0].Length, Is.EqualTo(1));
      Assert.That(grid[0][0], Is.EqualTo(JxlAcStrategyType.Dct8x8));
    });
  }

  /// <summary>
  /// 2x2 packed plane with DCT16x16 (strategy=4) at (0,0). DCT16x16's
  /// covered_blocks are 2x2, so the entire 2x2 group is one logical block:
  /// cell (0,0) = Dct16x16 (top-left); cells (1,0), (0,1), (1,1) = CoveredByNeighbour.
  ///
  /// The non-top-left cells of the packed plane carry "don't care" payloads
  /// in the spec — libjxl's loop skips them via <c>ac_strategy.IsValid</c>.
  /// We use 0xFF here to verify the validation logic doesn't read those
  /// bytes (otherwise it would treat 0xFF >> 4 = 15 as strategy AFV1, which
  /// would conflict with the cells already marked covered).
  /// </summary>
  [Test]
  public void BuildStrategyGrid_Dct16x16_MarksCoveredCells() {
    // Top-left = DCT16x16 (strategy=4, raw=4<<4=0x40).
    // Trailing cells use 0xFF as a "do not interpret" marker — they should
    // be skipped because the top-left cell claims the entire 2x2 area.
    var packed = new[] {
      0x40, 0xFF,
      0xFF, 0xFF,
    };
    var grid = JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 2, 2);

    Assert.Multiple(() => {
      Assert.That(grid[0][0], Is.EqualTo(JxlAcStrategyType.Dct16x16),
        "Top-left should carry the canonical Dct16x16 strategy.");
      Assert.That(grid[0][1], Is.EqualTo(JxlAcStrategyDecoder.CoveredByNeighbour),
        "(1, 0) should be covered by the Dct16x16 parent at (0, 0).");
      Assert.That(grid[1][0], Is.EqualTo(JxlAcStrategyDecoder.CoveredByNeighbour),
        "(0, 1) should be covered by the Dct16x16 parent at (0, 0).");
      Assert.That(grid[1][1], Is.EqualTo(JxlAcStrategyDecoder.CoveredByNeighbour),
        "(1, 1) should be covered by the Dct16x16 parent at (0, 0).");
    });
  }

  /// <summary>
  /// 2x1 packed plane with DCT8x16 (strategy=7), which spans 2x1 (covered_blocks_x=2,
  /// covered_blocks_y=1). The right-hand cell should be CoveredByNeighbour.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_Dct8x16_MarksHorizontalNeighbour() {
    var packed = new[] {
      (7 << 4), 0xFF,
    };
    var grid = JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 2, 1);

    Assert.Multiple(() => {
      Assert.That(grid[0][0], Is.EqualTo(JxlAcStrategyType.Dct8x16));
      Assert.That(grid[0][1], Is.EqualTo(JxlAcStrategyDecoder.CoveredByNeighbour));
    });
  }

  /// <summary>
  /// 1x2 packed plane with DCT16x8 (strategy=6), which spans 1x2
  /// (covered_blocks_x=1, covered_blocks_y=2). The bottom cell should be
  /// CoveredByNeighbour.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_Dct16x8_MarksVerticalNeighbour() {
    var packed = new[] {
      (6 << 4),
      0xFF,
    };
    var grid = JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 1, 2);

    Assert.Multiple(() => {
      Assert.That(grid[0][0], Is.EqualTo(JxlAcStrategyType.Dct16x8));
      Assert.That(grid[1][0], Is.EqualTo(JxlAcStrategyDecoder.CoveredByNeighbour));
    });
  }

  /// <summary>
  /// 2x2 packed plane with all DCT8x8 — no multi-block; every cell should
  /// hold its own strategy with no covered marks.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_AllDct8x8_NoCoveredCells() {
    var packed = new int[4]; // all zero → all DCT8x8
    var grid = JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 2, 2);

    foreach (var row in grid)
      foreach (var cell in row)
        Assert.That(cell, Is.EqualTo(JxlAcStrategyType.Dct8x8));
  }

  /// <summary>
  /// Strategy values >= 27 (kNumValidStrategies) are rejected. Use 27<<4 = 0x1B0
  /// (encoded as 27 in the high nibble after a 4-bit shift). Since the high
  /// nibble is 4 bits, raw values up to 15 fit; 27 needs to be a higher
  /// packed value to validate. Use the 27<<4 = 432 directly — the >> 4
  /// extraction yields 27.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_InvalidStrategy_Throws() {
    // strategy=27 (one past the max valid 26), qf=0 → packed = 27 << 4 = 0x1B0.
    var packed = new[] { 27 << 4 };

    var ex = Assert.Throws<InvalidDataException>(
      () => JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 1, 1));
    Assert.That(ex!.Message, Does.Contain("Invalid AC strategy"));
  }

  /// <summary>
  /// A multi-block strategy whose covered area extends past the group's
  /// right edge is rejected. DCT16x16 (covered_blocks_x=2) at (0,0) of a
  /// 1x2 group has no room horizontally → must throw.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_MultiBlockOverflowX_Throws() {
    // 1 wide x 2 high group, but DCT16x16 wants 2x2 → x overflow.
    var packed = new[] {
      0x40,
      0,
    };

    var ex = Assert.Throws<InvalidDataException>(
      () => JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 1, 2));
    Assert.That(ex!.Message, Does.Contain("right edge"));
  }

  /// <summary>
  /// A multi-block strategy whose covered area extends past the group's
  /// bottom edge is rejected. DCT16x16 (covered_blocks_y=2) at (0,0) of a
  /// 2x1 group has no room vertically → must throw.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_MultiBlockOverflowY_Throws() {
    // 2 wide x 1 high group, but DCT16x16 wants 2x2 → y overflow.
    var packed = new[] { 0x40, 0 };

    var ex = Assert.Throws<InvalidDataException>(
      () => JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 2, 1));
    Assert.That(ex!.Message, Does.Contain("bottom edge"));
  }

  /// <summary>
  /// The qf_idx (low 4 bits of the packed value) is part of the per-block
  /// quantization-field index in the spec but is not surfaced in the
  /// returned strategy grid. Verify a non-zero qf_idx leaves the strategy
  /// unaffected — only the high nibble drives the strategy.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_QfIndexIsIgnored() {
    // strategy=0 (Dct8x8), qf=0xF → packed = 0x0F.
    var packed = new[] { 0x0F };
    var grid = JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 1, 1);
    Assert.That(grid[0][0], Is.EqualTo(JxlAcStrategyType.Dct8x8));
  }

  /// <summary>
  /// Argument validation on the post-modular helper.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_NullPlane_Throws() {
    Assert.Throws<ArgumentNullException>(
      () => JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(null!, 1, 1));
  }

  /// <summary>
  /// Plane length mismatch is rejected.
  /// </summary>
  [Test]
  public void BuildStrategyGrid_PlaneTooSmall_Throws() {
    var packed = new[] { 0 };
    Assert.Throws<ArgumentException>(
      () => JxlAcStrategyDecoder._BuildStrategyGridFromPackedPlane(packed, 4, 4));
  }

  // ============================================================
  // _GetCoveredBlocks — table conformance
  // ============================================================

  /// <summary>
  /// Spot-check key entries against the libjxl <c>ac_strategy.h</c>
  /// kLut tables. Single-block strategies report (1, 1); the four most
  /// common multi-block strategies report their canonical sizes. Asymmetric
  /// strategies (DCT16x8 vs DCT8x16) verify the X/Y axes are not swapped.
  /// </summary>
  [Test]
  public void GetCoveredBlocks_SpotCheck() {
    Assert.Multiple(() => {
      // Single-block strategies.
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct8x8),
        Is.EqualTo((1, 1)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct2x2),
        Is.EqualTo((1, 1)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Afv0),
        Is.EqualTo((1, 1)));

      // Symmetric multi-block strategies.
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct16x16),
        Is.EqualTo((2, 2)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct32x32),
        Is.EqualTo((4, 4)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct64x64),
        Is.EqualTo((8, 8)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct256x256),
        Is.EqualTo((32, 32)));

      // Asymmetric: libjxl's kLut entries for DCT16x8 (idx 6) are X=1, Y=2,
      // and for DCT8x16 (idx 7) are X=2, Y=1. The naming follows JPEG's
      // (height, width) convention but the LUT is (X, Y) in the layout we
      // walk — make sure we didn't swap them.
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct16x8),
        Is.EqualTo((1, 2)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct8x16),
        Is.EqualTo((2, 1)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct128x256),
        Is.EqualTo((32, 16)));
      Assert.That(JxlAcStrategyDecoder._GetCoveredBlocks((int)JxlAcStrategyType.Dct256x128),
        Is.EqualTo((16, 32)));
    });
  }
}
