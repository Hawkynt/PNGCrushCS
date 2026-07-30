using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlColorCorrelationMap"/> (JPEG XL chroma-from-luma /
/// color-correlation map).
///
/// <para>libjxl reference (BSD-3-Clause):</para>
/// <list type="bullet">
///   <item><c>lib/jxl/chroma_from_luma.h</c> — ColorCorrelationMap struct</item>
///   <item><c>lib/jxl/chroma_from_luma.cc</c> — Create() zero-fills both maps</item>
///   <item><c>lib/jxl/dec_modular.cc DecodeAcMetadata</c> — channel 0 = ytox,
///         channel 1 = ytob</item>
///   <item><c>lib/jxl/dec_group.cc</c> — per-block lookup ty = by/8, then
///         row_cmap[abs_tx]</item>
/// </list>
/// </summary>
[TestFixture]
internal sealed class JxlColorCorrelationMapTests {

  // ---------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------

  /// <summary>Build a uniform set of 3 channels of <paramref name="numBlocks"/>
  /// 8×8 DCT blocks pre-filled with the given coefficient value.</summary>
  private static JxlDctBlock[][] _BuildUniformBlocks(int numBlocks, short xVal, short yVal, short bVal) {
    var x = new JxlDctBlock[numBlocks];
    var y = new JxlDctBlock[numBlocks];
    var b = new JxlDctBlock[numBlocks];
    for (var i = 0; i < numBlocks; ++i) {
      var xc = new short[64]; Array.Fill(xc, xVal);
      var yc = new short[64]; Array.Fill(yc, yVal);
      var bc = new short[64]; Array.Fill(bc, bVal);
      x[i] = new JxlDctBlock { Width = 8, Height = 8, Coefficients = xc };
      y[i] = new JxlDctBlock { Width = 8, Height = 8, Coefficients = yc };
      b[i] = new JxlDctBlock { Width = 8, Height = 8, Coefficients = bc };
    }
    return new[] { x, y, b };
  }

  // ---------------------------------------------------------------------
  // Constants — verify libjxl's published tile geometry.
  // ---------------------------------------------------------------------

  /// <summary>libjxl <c>kColorTileDim = 64</c>.</summary>
  [Test]
  public void Constants_MatchLibjxl() {
    Assert.Multiple(() => {
      Assert.That(JxlColorCorrelationMap.ColorTileDim, Is.EqualTo(64));
      Assert.That(JxlColorCorrelationMap.ColorTileDimInBlocks, Is.EqualTo(8));
      Assert.That(JxlColorCorrelationMap.DefaultColorFactor, Is.EqualTo(84));
    });
  }

  // ---------------------------------------------------------------------
  // CreateZero — libjxl's Create() + ZeroFillImage.
  // ---------------------------------------------------------------------

  /// <summary>For a 64×64 image we expect exactly 1×1 = 1 cmap tile (it is
  /// the boundary case where one tile covers the whole image).</summary>
  [Test]
  public void CreateZero_64x64Image_HasOneByOneTileGrid() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);

    Assert.Multiple(() => {
      Assert.That(cmap.TilesWide, Is.EqualTo(1));
      Assert.That(cmap.TilesHigh, Is.EqualTo(1));
      Assert.That(cmap.CmapX, Has.Length.EqualTo(1));
      Assert.That(cmap.CmapY, Has.Length.EqualTo(1));
      Assert.That(cmap.CmapX[0], Is.EqualTo(0).Within(0));
      Assert.That(cmap.CmapY[0], Is.EqualTo(0).Within(0));
    });
  }

  /// <summary>1024×1024 image → 16×16 = 256 tiles (the spec doc-comment example).</summary>
  [Test]
  public void CreateZero_1024x1024Image_Has16x16TileGrid() {
    var cmap = JxlColorCorrelationMap.CreateZero(1024, 1024);

    Assert.Multiple(() => {
      Assert.That(cmap.TilesWide, Is.EqualTo(16));
      Assert.That(cmap.TilesHigh, Is.EqualTo(16));
      Assert.That(cmap.CmapX, Has.Length.EqualTo(256));
      Assert.That(cmap.CmapY, Has.Length.EqualTo(256));
    });
  }

  /// <summary>Non-multiple dimensions round up (libjxl <c>DivCeil(xsize, kColorTileDim)</c>).</summary>
  [Test]
  public void CreateZero_NonMultipleDimensions_RoundsUp() {
    var cmap = JxlColorCorrelationMap.CreateZero(65, 130);

    Assert.Multiple(() => {
      Assert.That(cmap.TilesWide, Is.EqualTo(2)); // ceil(65/64) = 2
      Assert.That(cmap.TilesHigh, Is.EqualTo(3)); // ceil(130/64) = 3
    });
  }

  // ---------------------------------------------------------------------
  // FromModularChannels — libjxl DecodeAcMetadata channel 0/1.
  // ---------------------------------------------------------------------

  [Test]
  public void FromModularChannels_RoundTripValues_PreservesByteRangeValues() {
    var xPlane = new[] { 0, 5, -10, 100 };
    var yPlane = new[] { 1, -2, 0, -50 };

    var cmap = JxlColorCorrelationMap.FromModularChannels(xPlane, yPlane, tilesWide: 2, tilesHigh: 2);

    Assert.Multiple(() => {
      Assert.That((int)cmap.CmapX[0], Is.EqualTo(0));
      Assert.That((int)cmap.CmapX[1], Is.EqualTo(5));
      Assert.That((int)cmap.CmapX[2], Is.EqualTo(-10));
      Assert.That((int)cmap.CmapX[3], Is.EqualTo(100));
      Assert.That((int)cmap.CmapY[0], Is.EqualTo(1));
      Assert.That((int)cmap.CmapY[1], Is.EqualTo(-2));
      Assert.That((int)cmap.CmapY[2], Is.EqualTo(0));
      Assert.That((int)cmap.CmapY[3], Is.EqualTo(-50));
    });
  }

  [Test]
  public void FromModularChannels_OutOfRangeValues_ClampToSByte() {
    var xPlane = new[] { 200, -200 };       // out of sbyte range
    var yPlane = new[] { 1000, -1000 };

    var cmap = JxlColorCorrelationMap.FromModularChannels(xPlane, yPlane, tilesWide: 2, tilesHigh: 1);

    Assert.Multiple(() => {
      Assert.That((int)cmap.CmapX[0], Is.EqualTo(127));   // sbyte.MaxValue
      Assert.That((int)cmap.CmapX[1], Is.EqualTo(-128));  // sbyte.MinValue
      Assert.That((int)cmap.CmapY[0], Is.EqualTo(127));
      Assert.That((int)cmap.CmapY[1], Is.EqualTo(-128));
    });
  }

  [Test]
  public void FromModularChannels_LengthMismatch_Throws() {
    Assert.Throws<ArgumentException>(() =>
      JxlColorCorrelationMap.FromModularChannels(new[] { 0, 0 }, new[] { 0 }, 2, 1));
  }

  // ---------------------------------------------------------------------
  // GetTileIndex — per task spec: 64×64 image (1×1 tile), block (0,0)
  // and block (7,7) both map to tile (0,0).
  // ---------------------------------------------------------------------

  /// <summary>Per task spec: for a 64×64 image (1×1 tile), block (0,0)
  /// and block (7,7) both use tile (0,0).</summary>
  [Test]
  public void GetTileIndex_64x64Image_BlocksZeroAndSeven_BothMapToTileZero() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);

    Assert.Multiple(() => {
      Assert.That(cmap.GetTileIndex(0, 0), Is.EqualTo(0));
      Assert.That(cmap.GetTileIndex(7, 7), Is.EqualTo(0));
    });
  }

  /// <summary>For a 128×64 image (2×1 tiles), block (0,0)→tile 0, block
  /// (7,0)→tile 0, block (8,0)→tile 1, block (15,0)→tile 1.</summary>
  [Test]
  public void GetTileIndex_TileBoundaryAtBlockEight_SwitchesTiles() {
    var cmap = JxlColorCorrelationMap.CreateZero(128, 64);

    Assert.Multiple(() => {
      Assert.That(cmap.GetTileIndex(0, 0), Is.EqualTo(0));
      Assert.That(cmap.GetTileIndex(7, 0), Is.EqualTo(0));
      Assert.That(cmap.GetTileIndex(8, 0), Is.EqualTo(1));
      Assert.That(cmap.GetTileIndex(15, 0), Is.EqualTo(1));
    });
  }

  // ---------------------------------------------------------------------
  // ApplyCorrection — main correctness path.
  // ---------------------------------------------------------------------

  /// <summary>The trivial test required by the task spec: cmap with all
  /// zeros applies no correction (channels unchanged).</summary>
  [Test]
  public void ApplyCorrection_AllZeroCmap_ChannelsUnchanged() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);
    var blocks = _BuildUniformBlocks(numBlocks: 64, xVal: 100, yVal: 200, bVal: 300);

    cmap.ApplyCorrection(blocks, blocksWide: 8, blocksHigh: 8);

    Assert.Multiple(() => {
      for (var b = 0; b < 64; ++b) {
        for (var i = 0; i < 64; ++i) {
          Assert.That(blocks[(int)JxlVarDctChannel.X][b].Coefficients[i], Is.EqualTo(100), $"X block {b} coeff {i}");
          Assert.That(blocks[(int)JxlVarDctChannel.Y][b].Coefficients[i], Is.EqualTo(200), $"Y block {b} coeff {i}");
          Assert.That(blocks[(int)JxlVarDctChannel.B][b].Coefficients[i], Is.EqualTo(300), $"B block {b} coeff {i}");
        }
      }
    });
  }

  /// <summary>With cmap factor 128 (out-of-range but useful as a unit test
  /// because (128 * y) >> 7 = y), correction adds Y to X (and Y to B).</summary>
  [Test]
  public void ApplyCorrection_Factor128_AddsYToXAndB() {
    // Note: ApplyCorrection uses raw sbyte factors but does internal int math,
    // so we use 127 as the closest-to-128 representable value. (127 * y) >> 7
    // is approximately y * 0.992 — still adds nearly the full Y value.
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);
    cmap.CmapX[0] = 127;
    cmap.CmapY[0] = 127;
    var blocks = _BuildUniformBlocks(numBlocks: 64, xVal: 0, yVal: 100, bVal: 0);

    cmap.ApplyCorrection(blocks, blocksWide: 8, blocksHigh: 8);

    // (127 * 100) >> 7 = 12700 >> 7 = 99.
    Assert.Multiple(() => {
      Assert.That((int)blocks[(int)JxlVarDctChannel.X][0].Coefficients[0], Is.EqualTo(99));
      Assert.That((int)blocks[(int)JxlVarDctChannel.B][0].Coefficients[0], Is.EqualTo(99));
      Assert.That((int)blocks[(int)JxlVarDctChannel.Y][0].Coefficients[0], Is.EqualTo(100), "Y is read-only.");
    });
  }

  /// <summary>Negative factor pulls the signed correction in the negative
  /// direction (signed arithmetic-shift-right preserves sign).</summary>
  [Test]
  public void ApplyCorrection_NegativeFactor_SubtractsScaledY() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);
    cmap.CmapX[0] = -64;          // factor -64 / 128 = -0.5
    var blocks = _BuildUniformBlocks(numBlocks: 64, xVal: 50, yVal: 80, bVal: 0);

    cmap.ApplyCorrection(blocks, blocksWide: 8, blocksHigh: 8);

    // (-64 * 80) >> 7 = -5120 >> 7 = -40 (arithmetic shift).
    // X = 50 + (-40) = 10.
    Assert.That((int)blocks[(int)JxlVarDctChannel.X][0].Coefficients[0], Is.EqualTo(10));
  }

  /// <summary>Factors only affect the tile they are placed in. With a 2×1
  /// tile grid (128×64 image), setting the left tile's factor must not
  /// affect blocks in the right tile.</summary>
  [Test]
  public void ApplyCorrection_FactorsAreTileLocal() {
    var cmap = JxlColorCorrelationMap.CreateZero(128, 64);
    cmap.CmapX[0] = 127;          // left tile only
    // cmap.CmapX[1] stays 0       // right tile = no correction
    var blocks = _BuildUniformBlocks(numBlocks: 16 * 8, xVal: 0, yVal: 100, bVal: 0);

    cmap.ApplyCorrection(blocks, blocksWide: 16, blocksHigh: 8);

    Assert.Multiple(() => {
      // Left tile (block columns 0..7): X corrected to 99.
      Assert.That((int)blocks[(int)JxlVarDctChannel.X][0].Coefficients[0], Is.EqualTo(99));
      Assert.That((int)blocks[(int)JxlVarDctChannel.X][7].Coefficients[0], Is.EqualTo(99));
      // Right tile (block columns 8..15): X untouched.
      Assert.That((int)blocks[(int)JxlVarDctChannel.X][8].Coefficients[0], Is.EqualTo(0));
      Assert.That((int)blocks[(int)JxlVarDctChannel.X][15].Coefficients[0], Is.EqualTo(0));
    });
  }

  // ---------------------------------------------------------------------
  // YtoXRatio / YtoBRatio — libjxl's full-precision formula.
  // ---------------------------------------------------------------------

  /// <summary>libjxl: <c>YtoXRatio(0) = 0</c>.</summary>
  [Test]
  public void YtoXRatio_ZeroFactor_IsZero() {
    Assert.That(JxlColorCorrelationMap.YtoXRatio(0), Is.EqualTo(0.0f));
  }

  /// <summary>libjxl: <c>YtoXRatio(84) = 1.0</c> (factor / kDefaultColorFactor).</summary>
  [Test]
  public void YtoXRatio_FactorEqualsColorFactor_IsOne() {
    Assert.That(JxlColorCorrelationMap.YtoXRatio(84), Is.EqualTo(1.0f).Within(1e-6f));
    Assert.That(JxlColorCorrelationMap.YtoBRatio(84), Is.EqualTo(1.0f).Within(1e-6f));
  }
}
