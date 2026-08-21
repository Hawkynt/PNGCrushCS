using System;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// Where every block, super block and macro block of a frame is, and in what order the bitstream
/// walks them.
/// </summary>
/// <remarks>
/// Theora coordinates are right-handed: the origin is the *lower*-left corner of the frame and rows
/// count upwards. Every position in this class is in those coordinates, and the flip to the
/// top-down layout a bitmap wants happens once, when a picture is handed out.
/// <para/>
/// Three groupings overlap and none of them nests inside another cleanly. A block is 8x8 samples of
/// one plane. A super block is a 4x4 array of blocks of one plane, so it never spans planes and its
/// size in samples depends on which plane it is in. A macro block is a 2x2 array of luma blocks
/// *and* the chroma blocks covering the same picture, so it always spans all three planes and holds
/// between six and twelve blocks depending on the subsampling. Super blocks carry the coded flags;
/// macro blocks carry the coding mode and the motion vectors.
/// <para/>
/// The bitstream walks blocks in *coded order*, which is neither raster order nor anything
/// derivable from an index by arithmetic: super blocks in raster order, and within each one, the
/// blocks along a Hilbert curve — with any block that falls outside the plane simply left out, so
/// the count in a super block at the top or right edge is fewer than sixteen. DC prediction and the
/// loop filter, meanwhile, walk blocks in raster order. Both orders are needed, so both mappings are
/// built once per stream and looked up rather than computed.
/// <para/>
/// Theora specification sections 2.3 and 2.4, Figures 2.4 and 2.6.
/// </remarks>
internal sealed class TheoraGeometry {

  /// <summary>
  /// The order the sixteen blocks of a super block are walked, as (column, row) inside it.
  /// </summary>
  /// <remarks>
  /// Figure 2.4, read out with row zero at the bottom. The curve starts in the lower-left corner,
  /// crosses to the lower-right of the left half, doubles back up its left column, then mirrors the
  /// whole shape into the right half.
  /// </remarks>
  private static readonly (int X, int Y)[] _blockCurve = [
    (0, 0), (1, 0), (1, 1), (0, 1),
    (0, 2), (0, 3), (1, 3), (1, 2),
    (2, 2), (2, 3), (3, 3), (3, 2),
    (3, 1), (2, 1), (2, 0), (3, 0),
  ];

  /// <summary>The order the four macro blocks of a luma super block are walked — Figure 2.6.</summary>
  private static readonly (int X, int Y)[] _macroBlockCurve = [(0, 0), (0, 1), (1, 1), (1, 0)];

  /// <summary>The number of blocks along each side of a super block.</summary>
  internal const int BLOCKS_PER_SUPER_BLOCK_SIDE = 4;

  /// <summary>Samples along each side of a block.</summary>
  internal const int BLOCK_SIZE = 8;

  // -------- Per plane --------

  /// <summary>Each plane's width in samples.</summary>
  internal int[] PlaneWidth { get; } = new int[3];

  internal int[] PlaneHeight { get; } = new int[3];

  /// <summary>Each plane's width in blocks.</summary>
  internal int[] PlaneBlocksWide { get; } = new int[3];

  internal int[] PlaneBlocksHigh { get; } = new int[3];

  /// <summary>The coded-order index of the first block of each plane.</summary>
  /// <remarks>
  /// Well defined because coded order finishes one plane before starting the next, which is also
  /// what makes "is this block a luma block" a comparison rather than a lookup.
  /// </remarks>
  internal int[] PlaneFirstBlock { get; } = new int[3];

  // -------- Counts --------

  /// <summary>The total number of blocks in a frame, across all planes — NBS.</summary>
  internal int BlockCount { get; }

  /// <summary>The number of luma blocks, which are the first ones in coded order — NLBS.</summary>
  internal int LumaBlockCount { get; }

  /// <summary>The total number of super blocks in a frame, across all planes — NSBS.</summary>
  internal int SuperBlockCount { get; }

  /// <summary>The total number of macro blocks in a frame — NMBS.</summary>
  internal int MacroBlockCount { get; }

  /// <summary>How many chroma blocks of one plane a macro block covers: 1, 2 or 4.</summary>
  internal int ChromaBlocksPerMacroBlockPerPlane { get; }

  // -------- Per block, indexed in coded order --------

  /// <summary>Which colour plane each block belongs to.</summary>
  internal byte[] BlockPlane { get; }

  /// <summary>Each block's column within its own plane, counted in blocks.</summary>
  internal int[] BlockColumn { get; }

  /// <summary>Each block's row within its own plane, counted in blocks from the bottom.</summary>
  internal int[] BlockRow { get; }

  /// <summary>Which super block each block belongs to.</summary>
  internal int[] BlockSuperBlock { get; }

  /// <summary>Which macro block each block belongs to.</summary>
  internal int[] BlockMacroBlock { get; }

  /// <summary>How many blocks each super block actually holds, which is fewer than 16 at an edge.</summary>
  internal int[] SuperBlockBlockCount { get; }

  /// <summary>
  /// The coded-order index of the block at a raster position, laid out plane after plane.
  /// </summary>
  /// <remarks>
  /// Indexed by <c>PlaneFirstBlock[pli] + row * PlaneBlocksWide[pli] + column</c>, which works
  /// because each plane's blocks occupy one contiguous run of coded-order indices.
  /// </remarks>
  internal int[] RasterToCoded { get; }

  // -------- Per macro block, indexed in coded order --------

  /// <summary>
  /// The four luma blocks of each macro block, in raster order within it.
  /// </summary>
  /// <remarks>
  /// Four entries a macro block: lower-left, lower-right, upper-left, upper-right — the A, B, C and
  /// D of section 7.5.2. Raster order and not coded order, because that is the order the motion
  /// vectors of an INTER MV FOUR macro block are written in.
  /// </remarks>
  internal int[] MacroBlockLumaBlocks { get; }

  /// <summary>
  /// The chroma blocks of each macro block: the Cb ones in raster order, then the Cr ones.
  /// </summary>
  /// <remarks>
  /// <see cref="ChromaBlocksPerMacroBlockPerPlane"/> of each, so two entries a macro block for
  /// 4:2:0, four for 4:2:2 and eight for 4:4:4. These are the E through L of section 7.5.2.
  /// </remarks>
  internal int[] MacroBlockChromaBlocks { get; }

  /// <summary>Which macro block each luma block position maps to, laid out in macro block raster order.</summary>
  internal int[] MacroBlockAt { get; }

  internal TheoraGeometry(TheoraIdentificationHeader header) {
    var macroBlocksWide = header.FrameMacroBlocksWide;
    var macroBlocksHigh = header.FrameMacroBlocksHigh;

    this.PlaneWidth[0] = header.FrameWidth;
    this.PlaneHeight[0] = header.FrameHeight;
    this.PlaneWidth[1] = this.PlaneWidth[2] = header.ChromaWidth;
    this.PlaneHeight[1] = this.PlaneHeight[2] = header.ChromaHeight;

    for (var plane = 0; plane < 3; ++plane) {
      this.PlaneBlocksWide[plane] = this.PlaneWidth[plane] / BLOCK_SIZE;
      this.PlaneBlocksHigh[plane] = this.PlaneHeight[plane] / BLOCK_SIZE;
    }

    this.MacroBlockCount = macroBlocksWide * macroBlocksHigh;
    this.LumaBlockCount = this.MacroBlockCount * 4;

    var blocks = 0;
    var superBlocks = 0;
    for (var plane = 0; plane < 3; ++plane) {
      this.PlaneFirstBlock[plane] = blocks;
      blocks += this.PlaneBlocksWide[plane] * this.PlaneBlocksHigh[plane];
      superBlocks += _SuperBlocksAcross(this.PlaneBlocksWide[plane]) * _SuperBlocksAcross(this.PlaneBlocksHigh[plane]);
    }

    this.BlockCount = blocks;
    this.SuperBlockCount = superBlocks;

    this.BlockPlane = new byte[blocks];
    this.BlockColumn = new int[blocks];
    this.BlockRow = new int[blocks];
    this.BlockSuperBlock = new int[blocks];
    this.BlockMacroBlock = new int[blocks];
    this.SuperBlockBlockCount = new int[superBlocks];
    this.RasterToCoded = new int[blocks];

    this.ChromaBlocksPerMacroBlockPerPlane = header.PixelFormat switch {
      TheoraPixelFormat.Yuv420 => 1,
      TheoraPixelFormat.Yuv422 => 2,
      _ => 4,
    };

    this.MacroBlockLumaBlocks = new int[this.MacroBlockCount * 4];
    this.MacroBlockChromaBlocks = new int[this.MacroBlockCount * this.ChromaBlocksPerMacroBlockPerPlane * 2];
    this.MacroBlockAt = new int[this.MacroBlockCount];

    this._BuildMacroBlockOrder(macroBlocksWide, macroBlocksHigh);
    this._BuildCodedOrder(header, macroBlocksWide);
    this._BuildMacroBlockBlockLists(header, macroBlocksWide);
  }

  /// <summary>How many super blocks it takes to cover a run of blocks, rounding up.</summary>
  private static int _SuperBlocksAcross(int blocks) => (blocks + BLOCKS_PER_SUPER_BLOCK_SIDE - 1) / BLOCKS_PER_SUPER_BLOCK_SIDE;

  /// <summary>
  /// Numbers the macro blocks in coded order.
  /// </summary>
  /// <remarks>
  /// Luma super blocks in raster order, and within each one the four macro blocks along the small
  /// Hilbert curve of Figure 2.6, leaving out any that falls outside the frame. A luma super block
  /// is four blocks across, which is two macro blocks, so the super block grid here is half the
  /// macro block grid rounded up.
  /// </remarks>
  private void _BuildMacroBlockOrder(int macroBlocksWide, int macroBlocksHigh) {
    var superBlocksWide = (macroBlocksWide + 1) / 2;
    var superBlocksHigh = (macroBlocksHigh + 1) / 2;

    var next = 0;
    for (var superRow = 0; superRow < superBlocksHigh; ++superRow)
    for (var superColumn = 0; superColumn < superBlocksWide; ++superColumn)
      foreach (var (dx, dy) in _macroBlockCurve) {
        var column = superColumn * 2 + dx;
        var row = superRow * 2 + dy;
        if (column >= macroBlocksWide || row >= macroBlocksHigh)
          continue;

        this.MacroBlockAt[row * macroBlocksWide + column] = next++;
      }
  }

  /// <summary>
  /// Numbers the blocks in coded order, and records where each one is.
  /// </summary>
  /// <remarks>
  /// Plane after plane, super blocks in raster order, blocks along the Hilbert curve within each.
  /// The numbering runs on from one plane to the next rather than restarting, which is what lets a
  /// single array of coded flags cover the whole frame.
  /// </remarks>
  private void _BuildCodedOrder(TheoraIdentificationHeader header, int macroBlocksWide) {
    var next = 0;
    var superBlock = 0;

    for (var plane = 0; plane < 3; ++plane) {
      var blocksWide = this.PlaneBlocksWide[plane];
      var blocksHigh = this.PlaneBlocksHigh[plane];
      var superBlocksWide = _SuperBlocksAcross(blocksWide);
      var superBlocksHigh = _SuperBlocksAcross(blocksHigh);

      for (var superRow = 0; superRow < superBlocksHigh; ++superRow)
      for (var superColumn = 0; superColumn < superBlocksWide; ++superColumn, ++superBlock)
        foreach (var (dx, dy) in _blockCurve) {
          var column = superColumn * BLOCKS_PER_SUPER_BLOCK_SIDE + dx;
          var row = superRow * BLOCKS_PER_SUPER_BLOCK_SIDE + dy;

          // A super block at the top or right edge of a plane holds fewer than sixteen blocks. The
          // curve is walked in the same order regardless and the missing positions are skipped.
          if (column >= blocksWide || row >= blocksHigh)
            continue;

          this.BlockPlane[next] = (byte)plane;
          this.BlockColumn[next] = column;
          this.BlockRow[next] = row;
          this.BlockSuperBlock[next] = superBlock;
          this.BlockMacroBlock[next] = this.MacroBlockAt[
            _MacroBlockRowOf(header, plane, row) * macroBlocksWide + _MacroBlockColumnOf(header, plane, column)];
          ++this.SuperBlockBlockCount[superBlock];
          this.RasterToCoded[this.PlaneFirstBlock[plane] + row * blocksWide + column] = next;
          ++next;
        }
    }
  }

  /// <summary>Which macro block column a block of a plane falls in.</summary>
  /// <remarks>
  /// A macro block is two luma blocks across. In 4:2:0 and 4:2:2 the chroma planes are half as wide,
  /// so one chroma block spans a whole macro block; in 4:4:4 they are the same width as the luma
  /// plane and two chroma blocks span one.
  /// </remarks>
  private static int _MacroBlockColumnOf(TheoraIdentificationHeader header, int plane, int column)
    => plane == 0 || header.PixelFormat == TheoraPixelFormat.Yuv444 ? column / 2 : column;

  /// <summary>Which macro block row a block of a plane falls in.</summary>
  /// <remarks>Only 4:2:0 subsamples vertically, so only there does one chroma block span a macro block's height.</remarks>
  private static int _MacroBlockRowOf(TheoraIdentificationHeader header, int plane, int row)
    => plane == 0 || header.PixelFormat != TheoraPixelFormat.Yuv420 ? row / 2 : row;

  /// <summary>Collects each macro block's own blocks, in the raster order the bitstream needs them in.</summary>
  private void _BuildMacroBlockBlockLists(TheoraIdentificationHeader header, int macroBlocksWide) {
    var chromaPerPlane = this.ChromaBlocksPerMacroBlockPerPlane;

    for (var row = 0; row < header.FrameMacroBlocksHigh; ++row)
    for (var column = 0; column < macroBlocksWide; ++column) {
      var macroBlock = this.MacroBlockAt[row * macroBlocksWide + column];

      // A, B, C, D: lower-left, lower-right, upper-left, upper-right.
      for (var offset = 0; offset < 4; ++offset) {
        var blockColumn = column * 2 + (offset & 1);
        var blockRow = row * 2 + (offset >> 1);
        this.MacroBlockLumaBlocks[macroBlock * 4 + offset] = this.BlockAt(0, blockColumn, blockRow);
      }

      for (var plane = 1; plane <= 2; ++plane)
      for (var offset = 0; offset < chromaPerPlane; ++offset) {
        // 4:2:0 has one chroma block a macro block, 4:2:2 has two stacked, 4:4:4 has four in the
        // same arrangement as the luma blocks.
        var (blockColumn, blockRow) = header.PixelFormat switch {
          TheoraPixelFormat.Yuv420 => (column, row),
          TheoraPixelFormat.Yuv422 => (column, row * 2 + offset),
          _ => (column * 2 + (offset & 1), row * 2 + (offset >> 1)),
        };

        this.MacroBlockChromaBlocks[(macroBlock * 2 + plane - 1) * chromaPerPlane + offset] =
          this.BlockAt(plane, blockColumn, blockRow);
      }
    }
  }

  /// <summary>The coded-order index of the block at a raster position in a plane.</summary>
  internal int BlockAt(int plane, int column, int row)
    => this.RasterToCoded[this.PlaneFirstBlock[plane] + row * this.PlaneBlocksWide[plane] + column];

}
