using System;
using System.Collections.Generic;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9.Tests;

/// <summary>
/// Writes VP9 frames, so a test can state exactly which syntax it is exercising.
/// </summary>
/// <remarks>
/// Every stream in these tests is built rather than checked in. The decoder's arithmetic was settled
/// by decoding real streams here, in ffmpeg and in libvpx and comparing the sample planes; what that
/// cannot reach is twofold. The refusals, first, because a refusal is by definition unreachable from
/// a file an encoder would write — no encoder emits a reserved profile, a broken sync code, or a
/// frame that names a reference slot before one exists. And second the syntax libvpx has, but never
/// chooses: intra-only frames, the frame context resets, segmentation that states absolute values,
/// and the per-segment loop filter level.
/// <para/>
/// Only the uncompressed header of a VP9 frame is written as plain bits. Everything after it — the
/// compressed header and every tile — is arithmetic coded, so a frame cannot be assembled by writing
/// bits: it has to be encoded, with the same probabilities the decoder will read it with. The encoder
/// below is the counterpart of the decoder under test rather than a copy of it.
/// <para/>
/// The frames it writes carry no coefficients — every block declares itself skipped — but they do
/// carry a different intra mode in every block, so the picture is a patchwork of predictions rather
/// than a flat field. That matters: a flat picture would come out flat under almost any mistake,
/// where a picture built out of ten prediction directions changes if any one of them is read wrongly.
/// <para/>
/// The probability tables and coding trees come from the decoder's own, which means a test built on
/// them cannot catch a table that is wrong in both directions at once. That is what the comparison
/// against two independent decoders is for; these tests are about the paths that comparison cannot
/// reach.
/// </remarks>
internal sealed class Vp9TestStream {

  /// <summary>Which segments carry which adjustments (specification 6.2.11).</summary>
  internal sealed class Segmentation {

    /// <summary>Whether the feature values replace the frame's settings rather than adjusting them.</summary>
    internal bool AbsoluteValues;

    /// <summary>Which of the four features each of the eight segments carries.</summary>
    internal readonly bool[] Enabled = new bool[MAX_SEGMENTS * SEG_LVL_MAX];

    internal readonly int[] Data = new int[MAX_SEGMENTS * SEG_LVL_MAX];

    internal Segmentation Set(int segment, int feature, int value) {
      this.Enabled[segment * SEG_LVL_MAX + feature] = true;
      this.Data[segment * SEG_LVL_MAX + feature] = value;
      return this;
    }

    internal bool IsActive(int segment, int feature) => this.Enabled[segment * SEG_LVL_MAX + feature];
  }

  /// <summary>What a built frame says about itself, so that a test can make one field wrong.</summary>
  internal sealed class Options {

    internal int Width = 8;
    internal int Height = 8;
    internal int FrameMarker = 2;
    internal int Profile;
    internal int ColorSpace = CS_BT_601;
    internal byte[] SyncCode = [0x49, 0x83, 0x42];
    internal bool ShowFrame = true;

    /// <summary>
    /// An intra-only frame rather than a key frame, which is a frame the stream never shows.
    /// </summary>
    /// <remarks>
    /// Never shown because the format says so: <c>intra_only</c> is coded only when
    /// <c>show_frame</c> is zero, so an intra-only frame is always a reference and never a picture.
    /// </remarks>
    internal bool IntraOnly;

    internal int ResetFrameContext;
    internal int RefreshFrameFlags = 0xFF;
    internal int FrameContextIndex;
    internal bool ErrorResilient;

    internal int BaseQIndex = 100;
    internal int LoopFilterLevel;
    internal int LoopFilterSharpness;

    /// <summary>Whether to state the per-reference and per-mode filter adjustments.</summary>
    internal bool LoopFilterDeltas;

    /// <summary>The adjustment each reference frame's blocks get, when the frame states any.</summary>
    internal int[] ReferenceDeltas = [6, 1, -1, -2];

    internal int[] ModeDeltas = [2, -2];

    /// <summary>
    /// Whether every block predicts with the direct current mode, which makes the whole picture flat.
    /// </summary>
    /// <remarks>
    /// The first block has nothing above or to the left of it and comes out at 128; every block after
    /// it averages neighbours that are themselves 128. That is a picture a test can state rather than
    /// record, and one the loop filter must leave alone at any strength.
    /// </remarks>
    internal bool UniformMode;

    internal Segmentation? Segments;
  }

  // ============================================================================================
  // The boolean encoder — the counterpart of specification 9.2
  // ============================================================================================

  private readonly List<byte> _output = [];
  private uint _range = 255;
  private uint _bottom;
  private int _bitCount = 24;

  /// <summary>Writes one bool whose chance of being zero the reader will take as <paramref name="probability"/>/256.</summary>
  internal Vp9TestStream Bool(int probability, int value) {
    var split = 1 + (((this._range - 1) * (uint)probability) >> 8);
    if (value != 0) {
      this._bottom += split;
      this._range -= split;
    } else
      this._range = split;

    while (this._range < 128) {
      this._range <<= 1;
      if ((this._bottom & (1u << 31)) != 0)
        this._CarryIntoOutput();

      this._bottom <<= 1;
      if (--this._bitCount != 0)
        continue;

      this._output.Add((byte)(this._bottom >> 24));
      this._bottom &= (1u << 24) - 1;
      this._bitCount = 8;
    }

    return this;
  }

  /// <summary>Writes a bit at even odds, which is what <c>L(n)</c> is made of.</summary>
  internal Vp9TestStream Flag(int value) => this.Bool(128, value);

  /// <summary>Writes an unsigned value, high-order bit first, each bit at even odds.</summary>
  internal Vp9TestStream Literal(int bits, int value) {
    while (bits-- > 0)
      this.Flag((value >> bits) & 1);

    return this;
  }

  /// <summary>Writes the path through a tree that reaches <paramref name="value"/>.</summary>
  internal Vp9TestStream Tree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int value) {
    Span<int> path = stackalloc int[16];
    Span<int> bits = stackalloc int[16];
    var depth = _FindLeaf(tree, 0, value, path, bits, 0);

    if (depth < 0)
      throw new ArgumentException($"The tree has no leaf with the value {value}.", nameof(value));

    for (var i = 0; i < depth; ++i)
      this.Bool(probabilities[path[i] >> 1], bits[i]);

    return this;
  }

  private static int _FindLeaf(
    ReadOnlySpan<sbyte> tree, int node, int value, Span<int> path, Span<int> bits, int depth) {
    for (var bit = 0; bit < 2; ++bit) {
      var next = tree[node + bit];
      path[depth] = node;
      bits[depth] = bit;

      if (next <= 0) {
        if (-next == value)
          return depth + 1;

        continue;
      }

      var found = _FindLeaf(tree, next, value, path, bits, depth + 1);
      if (found >= 0)
        return found;
    }

    return -1;
  }

  /// <summary>Finishes the partition and hands back its bytes.</summary>
  internal byte[] Finish() {
    var count = this._bitCount;
    var value = this._bottom;

    if ((value & (1u << (32 - count))) != 0)
      this._CarryIntoOutput();

    value <<= count & 7;
    count >>= 3;
    while (--count >= 0)
      value <<= 8;

    count = 4;
    while (--count >= 0) {
      this._output.Add((byte)(value >> 24));
      value <<= 8;
    }

    return this._output.ToArray();
  }

  private void _CarryIntoOutput() {
    var at = this._output.Count - 1;
    while (at >= 0 && this._output[at] == 255) {
      this._output[at] = 0;
      --at;
    }

    if (at >= 0)
      ++this._output[at];
  }

  // ============================================================================================
  // Whole frames
  // ============================================================================================

  /// <summary>The seven probabilities a segment identity is written with, all at even odds.</summary>
  private static readonly byte[] _SegmentTreeProbabilities = [128, 128, 128, 128, 128, 128, 128];

  /// <summary>
  /// Builds an intra frame in which every block is 8x8 and skipped, and no two neighbouring blocks
  /// predict in the same direction.
  /// </summary>
  /// <remarks>
  /// A frame with no coefficients at all. Every block declares itself skipped, so the picture is
  /// whatever the ten intra prediction modes make of the invented edges the first block starts from
  /// and of each other's reconstructions — which is a picture with structure in it, and one the loop
  /// filter has real boundaries to work on.
  /// </remarks>
  internal static byte[] BuildKeyFrame(Options options) {
    var miColumns = (options.Width + 7) >> 3;
    var miRows = (options.Height + 7) >> 3;
    var superblockColumns = (miColumns + 7) >> 3;
    var superblockRows = (miRows + 7) >> 3;

    var compressed = _BuildCompressedHeader();
    var tile = _BuildTile(options, miColumns, miRows, superblockColumns, superblockRows);
    var header = _BuildUncompressedHeader(options, superblockColumns, compressed.Length);

    var frame = new byte[header.Length + compressed.Length + tile.Length];
    header.CopyTo(frame, 0);
    compressed.CopyTo(frame, header.Length);
    tile.CopyTo(frame, header.Length + compressed.Length);
    return frame;
  }

  /// <summary>
  /// Builds the one-byte frame that asks for a reference slot to be shown again.
  /// </summary>
  internal static byte[] BuildShowExistingFrame(int slot) {
    var writer = new BitWriter();
    writer.Literal(2, 2); // frame_marker
    writer.Literal(1, 0); // profile_low_bit
    writer.Literal(1, 0); // profile_high_bit
    writer.Literal(1, 1); // show_existing_frame
    writer.Literal(3, slot);
    return writer.Finish();
  }

  private static byte[] _BuildUncompressedHeader(Options options, int superblockColumns, int compressedLength) {
    var writer = new BitWriter();

    writer.Literal(2, options.FrameMarker);
    writer.Literal(1, options.Profile & 1);
    writer.Literal(1, (options.Profile >> 1) & 1);
    writer.Literal(1, 0); // show_existing_frame
    writer.Literal(1, options.IntraOnly ? NON_KEY_FRAME : KEY_FRAME);

    // An intra-only frame is coded as a frame that is not shown, because the flag that says it is
    // intra-only is only present when the frame is not shown.
    writer.Literal(1, options.IntraOnly ? 0 : options.ShowFrame ? 1 : 0);
    writer.Literal(1, options.ErrorResilient ? 1 : 0);

    if (options.IntraOnly) {
      writer.Literal(1, 1); // intra_only
      if (!options.ErrorResilient)
        writer.Literal(2, options.ResetFrameContext);

      foreach (var value in options.SyncCode)
        writer.Literal(8, value);

      // Profile 0 states no colour configuration on an intra-only frame: it is the 8-bit 4:2:0 the
      // sequence has been all along.
      writer.Literal(8, options.RefreshFrameFlags);
    } else {
      foreach (var value in options.SyncCode)
        writer.Literal(8, value);

      writer.Literal(3, options.ColorSpace);
      if (options.ColorSpace != CS_RGB)
        writer.Literal(1, 0); // color_range
    }

    writer.Literal(16, options.Width - 1);
    writer.Literal(16, options.Height - 1);
    writer.Literal(1, 0); // render_and_frame_size_different

    if (!options.ErrorResilient) {
      writer.Literal(1, 1); // refresh_frame_context
      writer.Literal(1, 0); // frame_parallel_decoding_mode
    }

    writer.Literal(2, options.FrameContextIndex);

    writer.Literal(6, options.LoopFilterLevel);
    writer.Literal(3, options.LoopFilterSharpness);
    writer.Literal(1, options.LoopFilterDeltas ? 1 : 0);
    if (options.LoopFilterDeltas) {
      writer.Literal(1, 1); // loop_filter_delta_update

      // Both signs are stated, so that a reader taking the sign flag for two's complement is caught.
      foreach (var delta in options.ReferenceDeltas) {
        writer.Literal(1, 1);
        writer.Literal(6, Math.Abs(delta));
        writer.Literal(1, delta < 0 ? 1 : 0);
      }

      foreach (var delta in options.ModeDeltas) {
        writer.Literal(1, 1);
        writer.Literal(6, Math.Abs(delta));
        writer.Literal(1, delta < 0 ? 1 : 0);
      }
    }

    writer.Literal(8, options.BaseQIndex);
    writer.Literal(1, 0); // delta_q_y_dc
    writer.Literal(1, 0); // delta_q_uv_dc
    writer.Literal(1, 0); // delta_q_uv_ac

    _WriteSegmentation(writer, options.Segments);

    // One tile column, which is the narrowest a picture may state. The flag that would widen it is
    // read only while the frame could still be wider than the minimum, and one zero stops the reader.
    var minimum = 0;
    while (MAX_TILE_WIDTH_B64 << minimum < superblockColumns)
      ++minimum;

    var maximum = 1;
    while (superblockColumns >> maximum >= MIN_TILE_WIDTH_B64)
      ++maximum;
    --maximum;

    if (minimum < maximum)
      writer.Literal(1, 0);

    writer.Literal(1, 0); // tile_rows_log2

    writer.Literal(16, compressedLength);
    return writer.Finish();
  }

  private static void _WriteSegmentation(BitWriter writer, Segmentation? segments) {
    if (segments == null) {
      writer.Literal(1, 0); // segmentation_enabled
      return;
    }

    writer.Literal(1, 1); // segmentation_enabled
    writer.Literal(1, 1); // segmentation_update_map

    foreach (var probability in _SegmentTreeProbabilities) {
      writer.Literal(1, 1); // prob_coded
      writer.Literal(8, probability);
    }

    writer.Literal(1, 0); // segmentation_temporal_update, which an intra frame has no map to predict from

    writer.Literal(1, 1); // segmentation_update_data
    writer.Literal(1, segments.AbsoluteValues ? 1 : 0);

    for (var segment = 0; segment < MAX_SEGMENTS; ++segment)
    for (var feature = 0; feature < SEG_LVL_MAX; ++feature) {
      var enabled = segments.Enabled[segment * SEG_LVL_MAX + feature];
      writer.Literal(1, enabled ? 1 : 0);
      if (!enabled)
        continue;

      var value = segments.Data[segment * SEG_LVL_MAX + feature];
      writer.Literal(Vp9Tables.SegmentationFeatureBits[feature], Math.Abs(value));
      if (Vp9Tables.SegmentationFeatureSigned[feature] != 0)
        writer.Literal(1, value < 0 ? 1 : 0);
    }
  }

  /// <summary>
  /// The compressed header of a frame carrying no coefficients: the marker, the transform mode, the
  /// flag that says the coefficient probabilities are not updated, and the three that say the same of
  /// the skip probabilities.
  /// </summary>
  private static byte[] _BuildCompressedHeader() {
    var writer = new Vp9TestStream();
    writer.Flag(0);          // the marker bit specification 9.2.1 requires
    writer.Literal(2, ONLY_4X4);
    writer.Flag(0);          // update_probs for the one transform size that mode uses
    for (var i = 0; i < SKIP_CONTEXTS; ++i)
      writer.Bool(252, 0);   // diff_update_prob for each skip probability

    return writer.Finish();
  }

  private static byte[] _BuildTile(
    Options options, int miColumns, int miRows, int superblockColumns, int superblockRows) {
    var writer = new Vp9TestStream();
    writer.Flag(0);

    var abovePartition = new byte[superblockColumns * 8];
    var leftPartition = new byte[superblockRows * 8];
    var modes = new byte[superblockColumns * 8 * superblockRows * 8];
    var stride = superblockColumns * 8;

    for (var row = 0; row < miRows; row += 8) {
      Array.Clear(leftPartition);
      for (var column = 0; column < miColumns; column += 8)
        _WritePartition(
          writer, options, row, column, BLOCK_64X64, miColumns, miRows, abovePartition, leftPartition, modes, stride);
    }

    return writer.Finish();
  }

  private static void _WritePartition(
    Vp9TestStream writer, Options options, int row, int column, int size, int miColumns, int miRows,
    byte[] abovePartition, byte[] leftPartition, byte[] modes, int stride) {
    if (row >= miRows || column >= miColumns)
      return;

    var blocks = Vp9Tables.Blocks8x8Wide[size];
    var half = blocks >> 1;
    var hasRows = row + half < miRows;
    var hasColumns = column + half < miColumns;

    var context = _PartitionContext(row, column, size, blocks, abovePartition, leftPartition);
    var probabilities = Vp9DefaultProbabilities.KeyFramePartition.Slice(
      context * (PARTITION_TYPES - 1), PARTITION_TYPES - 1);

    // Split all the way down to 8x8 and then take the whole block, which is the one partitioning
    // every picture size can express.
    var partition = size == BLOCK_8X8 ? PARTITION_NONE : PARTITION_SPLIT;

    if (hasRows && hasColumns)
      writer.Tree(Vp9Trees.Partition, probabilities, partition);
    else if (hasColumns)
      writer.Bool(probabilities[1], partition == PARTITION_SPLIT ? 1 : 0);
    else if (hasRows)
      writer.Bool(probabilities[2], partition == PARTITION_SPLIT ? 1 : 0);

    var subsize = Vp9Tables.SubsizeLookup[partition * BLOCK_SIZES + size];

    if (size == BLOCK_8X8)
      _WriteBlock(writer, options, row, column, modes, stride);
    else {
      _WritePartition(
        writer, options, row, column, subsize, miColumns, miRows, abovePartition, leftPartition, modes, stride);
      _WritePartition(
        writer, options, row, column + half, subsize, miColumns, miRows, abovePartition, leftPartition, modes, stride);
      _WritePartition(
        writer, options, row + half, column, subsize, miColumns, miRows, abovePartition, leftPartition, modes, stride);
      _WritePartition(
        writer, options, row + half, column + half, subsize, miColumns, miRows, abovePartition, leftPartition, modes,
        stride);
    }

    if (size != BLOCK_8X8 && partition == PARTITION_SPLIT)
      return;

    for (var i = 0; i < blocks; ++i) {
      abovePartition[column + i] = (byte)(15 >> Vp9Tables.BlockWidthLog2[subsize]);
      leftPartition[row + i] = (byte)(15 >> Vp9Tables.BlockHeightLog2[subsize]);
    }
  }

  private static int _PartitionContext(
    int row, int column, int size, int blocks, byte[] abovePartition, byte[] leftPartition) {
    var above = 0;
    var left = 0;
    var sizeLog2 = Vp9Tables.ModeInfoWidthLog2[size];
    var offset = Vp9Tables.ModeInfoWidthLog2[BLOCK_64X64] - sizeLog2;

    for (var i = 0; i < blocks; ++i) {
      above |= abovePartition[column + i];
      left |= leftPartition[row + i];
    }

    return sizeLog2 * 4
           + ((left & (1 << offset)) > 0 ? 2 : 0)
           + ((above & (1 << offset)) > 0 ? 1 : 0);
  }

  private static void _WriteBlock(Vp9TestStream writer, Options options, int row, int column, byte[] modes, int stride) {
    // Segments change every second block rather than every block. A chrominance plane reads its
    // block's settings from the even mode info position of each pair, so a segment map that
    // alternated with every block would put every chrominance block in an even-numbered segment and
    // leave half the map untested.
    var segment = options.Segments == null ? 0 : ((row >> 1) + (column >> 1)) % MAX_SEGMENTS;
    if (options.Segments != null)
      writer.Tree(Vp9Trees.Segment, _SegmentTreeProbabilities, segment);

    // A segment that carries the skip feature says so once in the frame header instead of once per
    // block, so nothing is written here for those blocks.
    if (options.Segments?.IsActive(segment, SEG_LVL_SKIP) != true) {
      // Every block is skipped, so the skip context is one for each neighbour that exists.
      var context = (row > 0 ? 1 : 0) + (column > 0 ? 1 : 0);
      writer.Bool(Vp9DefaultProbabilities.Skip[context], 1);
    }

    // A different direction in every block, so that no two neighbours agree and the picture has
    // structure the loop filter can act on.
    var mode = options.UniformMode ? DC_PRED : (row * 3 + column * 7) % INTRA_MODES;
    var above = row > 0 ? modes[(row - 1) * stride + column] : DC_PRED;
    var left = column > 0 ? modes[row * stride + column - 1] : DC_PRED;

    writer.Tree(
      Vp9Trees.IntraMode,
      Vp9DefaultProbabilities.KeyFrameYMode.Slice((above * INTRA_MODES + left) * (INTRA_MODES - 1), INTRA_MODES - 1),
      mode);

    var chromaMode = options.UniformMode ? DC_PRED : (mode + 3) % INTRA_MODES;
    writer.Tree(
      Vp9Trees.IntraMode,
      Vp9DefaultProbabilities.KeyFrameUvMode.Slice(mode * (INTRA_MODES - 1), INTRA_MODES - 1),
      chromaMode);

    modes[row * stride + column] = (byte)mode;
  }

  /// <summary>Writes the plain bits of an uncompressed header, most significant first.</summary>
  private sealed class BitWriter {

    private readonly List<byte> _bytes = [];
    private int _pending;
    private int _count;

    internal void Literal(int bits, int value) {
      while (bits-- > 0) {
        this._pending = (this._pending << 1) | ((value >> bits) & 1);
        if (++this._count != 8)
          continue;

        this._bytes.Add((byte)this._pending);
        this._pending = 0;
        this._count = 0;
      }
    }

    /// <summary>Pads to a byte boundary with the zero bits <c>trailing_bits</c> calls for.</summary>
    internal byte[] Finish() {
      while (this._count != 0)
        this.Literal(1, 0);

      return this._bytes.ToArray();
    }
  }
}
