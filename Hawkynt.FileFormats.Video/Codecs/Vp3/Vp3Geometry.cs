namespace FileFormat.Codecs.Vp3;

/// <summary>
/// How a VP3 frame of a given size is cut into blocks, super blocks and macro blocks, and the order
/// the bitstream walks them in.
/// </summary>
/// <remarks>
/// Three orders are in play at once and the format uses all of them. <b>Raster order</b> runs along
/// each row of blocks and then to the next row up; DC prediction and the loop filter go this way,
/// because each needs the neighbours it has already produced. <b>Coded order</b> walks super blocks —
/// four-by-four squares of blocks — in raster order, and the sixteen blocks inside each one along a
/// Hilbert curve; every flag, token and motion vector in the bitstream is in this order. And macro
/// blocks, which hold the coding mode and motion vector for the four luma blocks and the two chroma
/// blocks they cover, have a coded order of their own that walks the luma plane's super blocks and
/// takes the four macro blocks inside each along a smaller Hilbert curve.
/// <para/>
/// Block indices run continuously from the luma plane through both chroma planes, and so do super
/// block indices: they do not restart per plane, because the coded-block flags and the DCT tokens for
/// the whole frame are one sequence.
/// <para/>
/// <b>The vertical axis points up.</b> Row zero of a plane is the bottom row of the picture, which is
/// the coordinate system Theora and VP3 are written in, and keeping it means the specification's
/// "the block below" is this code's row minus one rather than something that has to be re-derived at
/// every use. The one place it shows is on the way out, in <see cref="Vp3ColorConversion"/>, which
/// reads the rows back to front.
/// </remarks>
internal sealed class Vp3Geometry {

  /// <summary>How many blocks fit along each side of a super block.</summary>
  private const int _SUPER_BLOCK_SIDE = 4;

  /// <summary>
  /// Where each of a super block's sixteen blocks sits, in the order the bitstream visits them.
  /// </summary>
  /// <remarks>
  /// The Hilbert curve of Figure 2.4, as (column, row) pairs with row zero at the bottom. Blocks that
  /// fall outside the frame — which happens on the top and right edges, where a super block need not
  /// be complete — are skipped, and the rest keep this order.
  /// </remarks>
  private static readonly (int Column, int Row)[] _BlockCurve = [
    (0, 0), (1, 0), (1, 1), (0, 1), (0, 2), (0, 3), (1, 3), (1, 2),
    (2, 2), (2, 3), (3, 3), (3, 2), (3, 1), (2, 1), (2, 0), (3, 0),
  ];

  /// <summary>Where each of a super block's four macro blocks sits, Figure 2.6.</summary>
  private static readonly (int Column, int Row)[] _MacroblockCurve = [(0, 0), (0, 1), (1, 1), (1, 0)];

  internal readonly int MacroblockColumns;
  internal readonly int MacroblockRows;

  /// <summary>The width of each plane in pixels: luma then both chroma planes.</summary>
  internal readonly int[] PlaneWidth;

  internal readonly int[] PlaneHeight;

  /// <summary>The width of each plane in blocks.</summary>
  internal readonly int[] PlaneBlockWidth;

  internal readonly int[] PlaneBlockHeight;

  /// <summary>The total number of blocks in a frame, across all three planes.</summary>
  internal readonly int BlockCount;

  /// <summary>The total number of super blocks in a frame, across all three planes.</summary>
  internal readonly int SuperBlockCount;

  internal readonly int MacroblockCount;

  /// <summary>How many of the blocks are in the luma plane, which is where the coefficient decode splits its codebooks.</summary>
  internal readonly int LumaBlockCount;

  /// <summary>The colour plane each block in coded order belongs to.</summary>
  internal readonly byte[] BlockPlane;

  /// <summary>The column and row, in blocks within its own plane, of each block in coded order.</summary>
  internal readonly int[] BlockColumn;

  internal readonly int[] BlockRow;

  /// <summary>The super block each block in coded order belongs to.</summary>
  internal readonly int[] BlockSuperBlock;

  /// <summary>The macro block each block in coded order belongs to.</summary>
  internal readonly int[] MacroblockOfBlock;

  /// <summary>
  /// Per plane, the coded-order index of the block at a raster position.
  /// </summary>
  /// <remarks>
  /// Walking one of these arrays from end to end is walking that plane's blocks in raster order,
  /// which is what DC prediction and the loop filter want.
  /// </remarks>
  internal readonly int[][] CodedIndex;

  /// <summary>
  /// The four luma blocks of each macro block in coded order, arranged into raster order.
  /// </summary>
  /// <remarks>Lower left, lower right, upper left, upper right, which is the order 7.5.2 reads four motion vectors in.</remarks>
  internal readonly int[][] MacroblockLumaBlocks;

  /// <summary>The Cb and Cr blocks of each macro block in coded order.</summary>
  internal readonly int[][] MacroblockChromaBlocks;

  internal Vp3Geometry(int macroblockColumns, int macroblockRows) {
    this.MacroblockColumns = macroblockColumns;
    this.MacroblockRows = macroblockRows;

    this.PlaneWidth = [macroblockColumns * 16, macroblockColumns * 8, macroblockColumns * 8];
    this.PlaneHeight = [macroblockRows * 16, macroblockRows * 8, macroblockRows * 8];
    this.PlaneBlockWidth = [macroblockColumns * 2, macroblockColumns, macroblockColumns];
    this.PlaneBlockHeight = [macroblockRows * 2, macroblockRows, macroblockRows];

    var blockCount = 6 * macroblockColumns * macroblockRows;
    this.BlockCount = blockCount;
    this.LumaBlockCount = 4 * macroblockColumns * macroblockRows;
    this.MacroblockCount = macroblockColumns * macroblockRows;

    this.BlockPlane = new byte[blockCount];
    this.BlockColumn = new int[blockCount];
    this.BlockRow = new int[blockCount];
    this.BlockSuperBlock = new int[blockCount];
    this.MacroblockOfBlock = new int[blockCount];
    this.CodedIndex = new int[3][];

    var block = 0;
    var superBlock = 0;
    for (var plane = 0; plane < 3; ++plane) {
      var width = this.PlaneBlockWidth[plane];
      var height = this.PlaneBlockHeight[plane];
      var index = new int[width * height];
      this.CodedIndex[plane] = index;

      var superBlockColumns = (width + _SUPER_BLOCK_SIDE - 1) / _SUPER_BLOCK_SIDE;
      var superBlockRows = (height + _SUPER_BLOCK_SIDE - 1) / _SUPER_BLOCK_SIDE;

      for (var superRow = 0; superRow < superBlockRows; ++superRow)
      for (var superColumn = 0; superColumn < superBlockColumns; ++superColumn, ++superBlock)
        foreach (var (curveColumn, curveRow) in _BlockCurve) {
          var column = superColumn * _SUPER_BLOCK_SIDE + curveColumn;
          var row = superRow * _SUPER_BLOCK_SIDE + curveRow;
          if (column >= width || row >= height)
            continue;

          index[row * width + column] = block;
          this.BlockPlane[block] = (byte)plane;
          this.BlockColumn[block] = column;
          this.BlockRow[block] = row;
          this.BlockSuperBlock[block] = superBlock;
          ++block;
        }
    }

    this.SuperBlockCount = superBlock;

    // Macro blocks, in the coded order of Figure 2.6. A super block of the luma plane is four blocks
    // on a side and so two macro blocks on a side.
    var macroblockIndex = new int[macroblockColumns * macroblockRows];
    var macroblockColumn = new int[macroblockColumns * macroblockRows];
    var macroblockRow = new int[macroblockColumns * macroblockRows];
    var macroblock = 0;
    var lumaSuperColumns = (macroblockColumns * 2 + _SUPER_BLOCK_SIDE - 1) / _SUPER_BLOCK_SIDE;
    var lumaSuperRows = (macroblockRows * 2 + _SUPER_BLOCK_SIDE - 1) / _SUPER_BLOCK_SIDE;

    for (var superRow = 0; superRow < lumaSuperRows; ++superRow)
    for (var superColumn = 0; superColumn < lumaSuperColumns; ++superColumn)
      foreach (var (curveColumn, curveRow) in _MacroblockCurve) {
        var column = superColumn * 2 + curveColumn;
        var row = superRow * 2 + curveRow;
        if (column >= macroblockColumns || row >= macroblockRows)
          continue;

        macroblockIndex[row * macroblockColumns + column] = macroblock;
        macroblockColumn[macroblock] = column;
        macroblockRow[macroblock] = row;
        ++macroblock;
      }

    this.MacroblockLumaBlocks = new int[this.MacroblockCount][];
    this.MacroblockChromaBlocks = new int[this.MacroblockCount][];
    var luma = this.CodedIndex[0];
    var lumaWidth = this.PlaneBlockWidth[0];

    for (var i = 0; i < this.MacroblockCount; ++i) {
      var column = macroblockColumn[i];
      var row = macroblockRow[i];
      this.MacroblockLumaBlocks[i] = [
        luma[row * 2 * lumaWidth + column * 2],
        luma[row * 2 * lumaWidth + column * 2 + 1],
        luma[(row * 2 + 1) * lumaWidth + column * 2],
        luma[(row * 2 + 1) * lumaWidth + column * 2 + 1],
      ];
      this.MacroblockChromaBlocks[i] = [
        this.CodedIndex[1][row * macroblockColumns + column],
        this.CodedIndex[2][row * macroblockColumns + column],
      ];
    }

    for (var i = 0; i < blockCount; ++i) {
      var plane = this.BlockPlane[i];
      var column = plane == 0 ? this.BlockColumn[i] >> 1 : this.BlockColumn[i];
      var row = plane == 0 ? this.BlockRow[i] >> 1 : this.BlockRow[i];
      this.MacroblockOfBlock[i] = macroblockIndex[row * macroblockColumns + column];
    }
  }
}
