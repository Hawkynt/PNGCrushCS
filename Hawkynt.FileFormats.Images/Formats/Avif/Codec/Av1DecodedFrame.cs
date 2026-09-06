using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Sample planes plus the per-4x4 mode information the AV1 in-loop filters need after the tiles have
/// been decoded. Everything is stored at frame scope rather than per tile because deblocking, CDEF
/// and loop restoration all cross tile boundaries.
/// </summary>
internal sealed class Av1DecodedFrame {

  public Av1DecodedFrame(Av1SequenceHeader seq, int frameWidth, int frameHeight) {
    this.BitDepth = seq.BitDepth;
    this.NumPlanes = seq.NumPlanes;
    this.Width = frameWidth;
    this.Height = frameHeight;

    // AV1 compute_image_size(): the mode-info grid covers whole 8x8 luma regions, so an odd frame
    // size still gets an even number of 4x4 columns and rows.
    this.MiCols = 2 * ((frameWidth + 7) >> 3);
    this.MiRows = 2 * ((frameHeight + 7) >> 3);

    this.SubX = new int[this.NumPlanes];
    this.SubY = new int[this.NumPlanes];
    this.Planes = new short[this.NumPlanes][];
    this.Strides = new int[this.NumPlanes];
    this.Widths = new int[this.NumPlanes];
    this.Heights = new int[this.NumPlanes];
    this.TxSizes = new byte[this.NumPlanes][];

    var miCount = this.MiRows * this.MiCols;
    for (var plane = 0; plane < this.NumPlanes; ++plane) {
      var subX = plane > 0 ? seq.SubsamplingX : 0;
      var subY = plane > 0 ? seq.SubsamplingY : 0;
      this.SubX[plane] = subX;
      this.SubY[plane] = subY;

      // Widths and Heights are the extent AV1 calls valid — the mode-info grid, not the visible
      // frame. The allocation is larger again: a transform block on the right or bottom edge of a
      // partly-outside superblock is still written in full, so the buffer has to hold it.
      this.Widths[plane] = (this.MiCols * 4) >> subX;
      this.Heights[plane] = (this.MiRows * 4) >> subY;
      this.Strides[plane] = this.Widths[plane] + Av1Constants.MaxTxSize;
      this.Planes[plane] = new short[this.Strides[plane] * (this.Heights[plane] + Av1Constants.MaxTxSize)];
      this.TxSizes[plane] = new byte[miCount];
    }

    this.BlockSizes = new byte[miCount];
    this.YModes = new byte[miCount];
    this.UvModes = new byte[miCount];
    this.Skips = new bool[miCount];
    this.SegmentIds = new byte[miCount];
    this.DeltaLf = new sbyte[miCount * 4];
    this.PaletteSizes = new byte[miCount * 2];

    var sb64Cols = (this.MiCols + 15) >> 4;
    var sb64Rows = (this.MiRows + 15) >> 4;
    this.Cdef64Cols = sb64Cols;
    this.CdefIndices = new sbyte[sb64Cols * sb64Rows];
    Array.Fill(this.CdefIndices, (sbyte)-1);
  }

  public int BitDepth { get; }
  public int NumPlanes { get; }

  /// <summary>Visible frame width in luma samples; the plane buffers are wider.</summary>
  public int Width { get; }

  /// <summary>Visible frame height in luma samples; the plane buffers are taller.</summary>
  public int Height { get; }

  public int MiCols { get; }
  public int MiRows { get; }

  public int[] SubX { get; }
  public int[] SubY { get; }
  public short[][] Planes { get; }
  public int[] Strides { get; }
  public int[] Widths { get; }
  public int[] Heights { get; }

  /// <summary>Transform size in use at each 4x4 position, per plane, as a TX_SIZE.</summary>
  public byte[][] TxSizes { get; }

  /// <summary>BLOCK_SIZE of the prediction block covering each 4x4 position.</summary>
  public byte[] BlockSizes { get; }

  /// <summary>Luma prediction mode at each 4x4 position.</summary>
  public byte[] YModes { get; }

  /// <summary>Chroma prediction mode at each 4x4 position.</summary>
  public byte[] UvModes { get; }

  /// <summary>Whether the block covering each 4x4 position coded no residual.</summary>
  public bool[] Skips { get; }

  public byte[] SegmentIds { get; }

  /// <summary>Per-4x4 loop-filter deltas, four entries per position (y vertical, y horizontal, u, v).</summary>
  public sbyte[] DeltaLf { get; }

  /// <summary>Palette size for luma and chroma at each 4x4 position, two entries per position.</summary>
  public byte[] PaletteSizes { get; }

  /// <summary>CDEF filter index per 64x64 unit, -1 where no unit was coded.</summary>
  public sbyte[] CdefIndices { get; }

  /// <summary>Width of the CDEF index grid in 64x64 units.</summary>
  public int Cdef64Cols { get; }

  public short[] Plane(int index) => this.Planes[index];
}
