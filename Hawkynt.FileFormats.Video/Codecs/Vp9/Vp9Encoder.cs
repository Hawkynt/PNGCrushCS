using System;
using System.IO;
using FileFormat.Core;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>Writes self-contained lossless VP9 profile-0 keyframes.</summary>
/// <remarks>
/// The encoder deliberately chooses a small, exact subset of VP9 rather than pretending to be a
/// rate-distortion encoder. Pictures are eight-bit 4:2:0, every frame is a keyframe, every block is
/// intra-predicted with DC, transforms are the lossless 4x4 Walsh-Hadamard transform, the quantiser is
/// one, probabilities stay at their normative defaults, and there is one tile. This produces ordinary
/// interoperable VP9 while keeping every source Y, Cb and Cr sample bit-exact.
/// </remarks>
internal static class Vp9Encoder {

  private const int _MAX_SINGLE_TILE_WIDTH = MAX_TILE_WIDTH_B64 * 64;

  internal static byte[] Encode(RawImage source) {
    ArgumentNullException.ThrowIfNull(source);

    if (source.Width <= 0 || source.Height <= 0 || source.Width > _MAX_SINGLE_TILE_WIDTH || source.Height > 65536)
      throw new NotSupportedException(
        $"This VP9 writer emits one tile and therefore supports pictures from 1x1 through "
        + $"{_MAX_SINGLE_TILE_WIDTH}x65536; this picture is {source.Width}x{source.Height}.");

    if (source.Format != PixelFormat.Yuv420P8)
      throw new NotSupportedException(
        $"The VP9 writer is lossless and currently codes native {PixelFormat.Yuv420P8} only; converting "
        + $"{source.Format} to 4:2:0 here would change samples before the codec saw them.");

    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {source.Width}x{source.Height} {source.Format} picture needs at least {source.MinimumPixelDataLength} "
        + $"bytes of samples and this one carries {source.PixelData?.Length ?? 0}.");

    var miColumns = (source.Width + 7) >> 3;
    var miRows = (source.Height + 7) >> 3;
    var superblockColumns = (miColumns + 7) >> 3;
    var superblockRows = (miRows + 7) >> 3;

    var probabilities = new Vp9Probabilities();
    probabilities.Reset();

    var compressed = _BuildCompressedHeader();
    var tile = new FrameWriter(source, probabilities, miColumns, miRows, superblockColumns, superblockRows).BuildTile();
    var header = _BuildUncompressedHeader(
      source, superblockColumns, compressed.Length, _ColorSpace(source.ColorInfo), _ColorRange(source.ColorInfo));

    var frame = new byte[checked(header.Length + compressed.Length + tile.Length)];
    header.CopyTo(frame, 0);
    compressed.CopyTo(frame, header.Length);
    tile.CopyTo(frame, header.Length + compressed.Length);
    return frame;
  }

  private static int _ColorSpace(RawImageColorInfo? color) => color?.Matrix switch {
    RawMatrixCoefficients.Bt709 => 2,
    RawMatrixCoefficients.Bt601 => CS_BT_601,
    RawMatrixCoefficients.Smpte240M => 4,
    RawMatrixCoefficients.Bt2020NonConstantLuminance or RawMatrixCoefficients.Bt2020ConstantLuminance => 5,
    _ => CS_UNKNOWN,
  };

  private static int _ColorRange(RawImageColorInfo? color) => color?.Range == RawColorRange.Full ? 1 : 0;

  // ============================================================================================
  // Frame headers
  // ============================================================================================

  private static byte[] _BuildCompressedHeader() {
    var writer = new Vp9BoolEncoder();
    writer.WriteFlag(0);        // marker bit (9.2.1)

    // Lossless forces ONLY_4X4, so tx_mode consumes no bits. One flag says the sole coefficient
    // probability set is unchanged, then the three skip probabilities say the same.
    writer.WriteFlag(0);
    for (var context = 0; context < SKIP_CONTEXTS; ++context)
      writer.WriteBool(252, 0);

    return writer.Finish();
  }

  private static byte[] _BuildUncompressedHeader(
    RawImage source, int superblockColumns, int compressedLength, int colorSpace, int colorRange) {
    var writer = new BitWriter();

    writer.Literal(2, 2);             // frame_marker
    writer.Literal(1, 0);             // profile_low_bit
    writer.Literal(1, 0);             // profile_high_bit: profile 0
    writer.Literal(1, 0);             // show_existing_frame
    writer.Literal(1, KEY_FRAME);
    writer.Literal(1, 1);             // show_frame
    writer.Literal(1, 0);             // error_resilient_mode
    writer.Literal(8, 0x49);
    writer.Literal(8, 0x83);
    writer.Literal(8, 0x42);
    writer.Literal(3, colorSpace);
    writer.Literal(1, colorRange);
    writer.Literal(16, source.Width - 1);
    writer.Literal(16, source.Height - 1);
    writer.Literal(1, 0);             // render_and_frame_size_different
    writer.Literal(1, 1);             // refresh_frame_context
    writer.Literal(1, 0);             // frame_parallel_decoding_mode
    writer.Literal(2, 0);             // frame_context_idx
    writer.Literal(6, 0);             // loop_filter_level
    writer.Literal(3, 0);             // loop_filter_sharpness
    writer.Literal(1, 0);             // mode_ref_delta_enabled
    writer.Literal(8, 0);             // base_q_idx: lossless
    writer.Literal(1, 0);             // delta_q_y_dc
    writer.Literal(1, 0);             // delta_q_uv_dc
    writer.Literal(1, 0);             // delta_q_uv_ac
    writer.Literal(1, 0);             // segmentation_enabled

    // Width is restricted to one legal tile column. The syntax still contains a stop flag when a
    // picture is wide enough that the decoder could choose two.
    var minimum = 0;
    while (MAX_TILE_WIDTH_B64 << minimum < superblockColumns)
      ++minimum;

    var maximum = 1;
    while (superblockColumns >> maximum >= MIN_TILE_WIDTH_B64)
      ++maximum;
    --maximum;

    if (minimum < maximum)
      writer.Literal(1, 0);            // stop at one tile column

    writer.Literal(1, 0);              // tile_rows_log2
    writer.Literal(16, compressedLength);
    return writer.Finish();
  }

  // ============================================================================================
  // Tile, partitions, prediction and coefficients
  // ============================================================================================

  private sealed class FrameWriter {

    private readonly RawImage _source;
    private readonly Vp9Probabilities _probabilities;
    private readonly int _miColumns;
    private readonly int _miRows;
    private readonly Vp9Frame _reconstructed;
    private readonly byte[][] _aboveNonzero = [[], [], []];
    private readonly byte[][] _leftNonzero = [[], [], []];
    private readonly byte[] _abovePartition;
    private readonly byte[] _leftPartition;

    internal FrameWriter(
      RawImage source, Vp9Probabilities probabilities,
      int miColumns, int miRows, int superblockColumns, int superblockRows) {
      this._source = source;
      this._probabilities = probabilities;
      this._miColumns = miColumns;
      this._miRows = miRows;
      this._reconstructed = new(
        source.Width, source.Height, superblockColumns, superblockRows,
        8, 1, 1, _ColorSpace(source.ColorInfo), _ColorRange(source.ColorInfo));

      var aboveLength = miColumns * 2 + 32;
      var leftLength = miRows * 2 + 32;
      for (var plane = 0; plane < 3; ++plane) {
        this._aboveNonzero[plane] = new byte[aboveLength];
        this._leftNonzero[plane] = new byte[leftLength];
      }

      this._abovePartition = new byte[superblockColumns * 8];
      this._leftPartition = new byte[superblockRows * 8];
    }

    internal byte[] BuildTile() {
      var writer = new Vp9BoolEncoder();
      writer.WriteFlag(0); // tile marker

      for (var row = 0; row < this._miRows; row += 8) {
        foreach (var plane in this._leftNonzero)
          Array.Clear(plane);
        Array.Clear(this._leftPartition);

        for (var column = 0; column < this._miColumns; column += 8)
          this._WritePartition(writer, row, column, BLOCK_64X64);
      }

      return writer.Finish();
    }

    private void _WritePartition(Vp9BoolEncoder writer, int row, int column, int size) {
      if (row >= this._miRows || column >= this._miColumns)
        return;

      var blocks = Vp9Tables.Blocks8x8Wide[size];
      var half = blocks >> 1;
      var hasRows = row + half < this._miRows;
      var hasColumns = column + half < this._miColumns;
      var context = this._PartitionContext(row, column, size, blocks);
      var probabilities = Vp9DefaultProbabilities.KeyFramePartition.Slice(
        context * (PARTITION_TYPES - 1), PARTITION_TYPES - 1);
      var partition = size == BLOCK_8X8 ? PARTITION_NONE : PARTITION_SPLIT;

      if (hasRows && hasColumns)
        writer.WriteTree(Vp9Trees.Partition, probabilities, partition);
      else if (hasColumns)
        writer.WriteBool(probabilities[1], partition == PARTITION_SPLIT ? 1 : 0);
      else if (hasRows)
        writer.WriteBool(probabilities[2], partition == PARTITION_SPLIT ? 1 : 0);

      var subsize = Vp9Tables.SubsizeLookup[partition * BLOCK_SIZES + size];
      if (size == BLOCK_8X8)
        this._WriteBlock(writer, row, column);
      else {
        this._WritePartition(writer, row, column, subsize);
        this._WritePartition(writer, row, column + half, subsize);
        this._WritePartition(writer, row + half, column, subsize);
        this._WritePartition(writer, row + half, column + half, subsize);
      }

      if (size != BLOCK_8X8 && partition == PARTITION_SPLIT)
        return;

      var aboveMark = (byte)(15 >> Vp9Tables.BlockWidthLog2[subsize]);
      var leftMark = (byte)(15 >> Vp9Tables.BlockHeightLog2[subsize]);
      for (var i = 0; i < blocks; ++i) {
        this._abovePartition[column + i] = aboveMark;
        this._leftPartition[row + i] = leftMark;
      }
    }

    private int _PartitionContext(int row, int column, int size, int blocks) {
      var above = 0;
      var left = 0;
      var sizeLog2 = Vp9Tables.ModeInfoWidthLog2[size];
      var offset = Vp9Tables.ModeInfoWidthLog2[BLOCK_64X64] - sizeLog2;

      for (var i = 0; i < blocks; ++i) {
        above |= this._abovePartition[column + i];
        left |= this._leftPartition[row + i];
      }

      return sizeLog2 * 4
             + ((left & (1 << offset)) != 0 ? 2 : 0)
             + ((above & (1 << offset)) != 0 ? 1 : 0);
    }

    private void _WriteBlock(Vp9BoolEncoder writer, int row, int column) {
      writer.WriteBool(this._probabilities.Skip[0], 0);

      writer.WriteTree(
        Vp9Trees.IntraMode,
        Vp9DefaultProbabilities.KeyFrameYMode.Slice(0, INTRA_MODES - 1),
        DC_PRED);
      writer.WriteTree(
        Vp9Trees.IntraMode,
        Vp9DefaultProbabilities.KeyFrameUvMode.Slice(0, INTRA_MODES - 1),
        DC_PRED);

      for (var plane = 0; plane < 3; ++plane)
        this._WritePlane(writer, plane, row, column);
    }

    private void _WritePlane(Vp9BoolEncoder writer, int plane, int miRow, int miColumn) {
      var subX = plane == 0 ? 0 : 1;
      var subY = plane == 0 ? 0 : 1;
      var baseX = (miColumn * 8) >> subX;
      var baseY = (miRow * 8) >> subY;
      var wide = plane == 0 ? 2 : 1;
      var high = plane == 0 ? 2 : 1;
      var maxX = (this._miColumns * 8) >> subX;
      var maxY = (this._miRows * 8) >> subY;
      var samples = this._reconstructed.Plane(plane);
      var stride = this._reconstructed.Stride(plane);
      var source = this._source.GetPlaneData(plane);
      var (sourceWidth, sourceHeight) = this._source.GetPlaneDimensions(plane);
      var availableAbove = miRow > 0;
      var availableLeft = miColumn > 0;

      Span<int> residual = stackalloc int[16];
      Span<int> coefficients = stackalloc int[16];
      for (var y = 0; y < high; ++y)
      for (var x = 0; x < wide; ++x) {
        var startX = baseX + x * 4;
        var startY = baseY + y * 4;

        Vp9IntraPrediction.Predict(
          samples, stride, startX, startY, 2, DC_PRED,
          availableLeft || x > 0, availableAbove || y > 0, x + 1 < wide,
          maxX - 1, maxY - 1, 8);

        for (var localY = 0; localY < 4; ++localY)
        for (var localX = 0; localX < 4; ++localX) {
          var px = startX + localX;
          var py = startY + localY;
          var reconstructedAt = py * stride + px;
          var predicted = samples[reconstructedAt];
          var target = px < sourceWidth && py < sourceHeight
            ? (ushort)source[py * sourceWidth + px]
            : predicted;

          residual[localY * 4 + localX] = target - predicted;
          samples[reconstructedAt] = target;
        }

        Vp9ForwardTransform.WalshHadamard4x4(residual, coefficients);
        var nonzero = this._WriteTokens(writer, plane, startX, startY, coefficients);
        this._aboveNonzero[plane][startX >> 2] = nonzero;
        this._leftNonzero[plane][startY >> 2] = nonzero;
      }
    }

    private byte _WriteTokens(
      Vp9BoolEncoder writer, int plane, int startX, int startY, ReadOnlySpan<int> coefficients) {
      var scan = Vp9Tables.DefaultScan4x4;
      var bands = Vp9Tables.CoefficientBand4x4;
      Span<byte> tokenCache = stackalloc byte[16];

      var last = -1;
      for (var c = 15; c >= 0; --c)
        if (coefficients[scan[c]] != 0) {
          last = c;
          break;
        }

      var context = this._FirstCoefficientContext(plane, startX, startY);
      if (last < 0) {
        var at = CoefficientContext(TX_4X4, plane > 0 ? 1 : 0, 0, bands[0], context);
        writer.WriteBool(this._probabilities.Coefficient[at * UNCONSTRAINED_NODES], 0);
        return 0;
      }

      var checkEndOfBlock = true;
      for (var c = 0; c <= last; ++c) {
        var position = scan[c];
        if (c > 0)
          context = _NeighbourContext(position, tokenCache);

        var at = CoefficientContext(TX_4X4, plane > 0 ? 1 : 0, 0, bands[c], context);
        if (checkEndOfBlock)
          writer.WriteBool(this._probabilities.Coefficient[at * UNCONSTRAINED_NODES], 1);

        var value = coefficients[position];
        var magnitude = Math.Abs(value);
        var token = _TokenOf(magnitude);
        this._WriteToken(writer, at, token);
        tokenCache[position] = Vp9Tables.EnergyClass[token];

        if (token == ZERO_TOKEN) {
          checkEndOfBlock = false;
          continue;
        }

        _WriteCoefficient(writer, token, magnitude);
        writer.WriteFlag(value < 0 ? 1 : 0);
        checkEndOfBlock = true;
      }

      if (last < 15) {
        var c = last + 1;
        var position = scan[c];
        context = _NeighbourContext(position, tokenCache);
        var at = CoefficientContext(TX_4X4, plane > 0 ? 1 : 0, 0, bands[c], context);
        writer.WriteBool(this._probabilities.Coefficient[at * UNCONSTRAINED_NODES], 0);
      }

      return 1;
    }

    private int _FirstCoefficientContext(int plane, int startX, int startY) {
      var above = this._aboveNonzero[plane][startX >> 2];
      var left = this._leftNonzero[plane][startY >> 2];
      return above + left;
    }

    private static int _NeighbourContext(int position, ReadOnlySpan<byte> tokenCache) {
      var row = position >> 2;
      var column = position & 3;

      int first;
      int second;
      if (row > 0 && column > 0) {
        first = (row - 1) * 4 + column;
        second = row * 4 + column - 1;
      } else if (row > 0)
        first = second = (row - 1) * 4 + column;
      else
        first = second = row * 4 + column - 1;

      return (1 + tokenCache[first] + tokenCache[second]) >> 1;
    }

    private void _WriteToken(Vp9BoolEncoder writer, int context, int token) {
      Span<byte> probabilities = stackalloc byte[10];
      for (var level = 0; level < probabilities.Length; ++level) {
        var probability = this._probabilities.Coefficient[
          context * UNCONSTRAINED_NODES + Math.Min(2, 1 + level)];
        probabilities[level] = (byte)_Pareto(level, probability);
      }

      writer.WriteTree(Vp9Trees.Token, probabilities, token);
    }

    private static int _Pareto(int node, int probability) {
      if (node < 2)
        return probability;

      var row = (probability - 1) / 2;
      var column = node - 2;
      return (probability & 1) != 0
        ? Vp9Tables.ParetoTable[row * 8 + column]
        : (Vp9Tables.ParetoTable[row * 8 + column] + Vp9Tables.ParetoTable[(row + 1) * 8 + column]) >> 1;
    }

    private static int _TokenOf(int magnitude) {
      if (magnitude <= FOUR_TOKEN)
        return magnitude;

      for (var token = DCT_VAL_CATEGORY1; token <= DCT_VAL_CATEGORY6; ++token) {
        var maximum = Vp9Tables.TokenBaseValue[token] + (1 << Vp9Tables.TokenExtraBits[token]) - 1;
        if (magnitude <= maximum)
          return token;
      }

      throw new InvalidDataException($"Lossless VP9 produced coefficient {magnitude}, which category six cannot represent.");
    }

    private static void _WriteCoefficient(Vp9BoolEncoder writer, int token, int magnitude) {
      var category = Vp9Tables.TokenCategory[token];
      var bits = Vp9Tables.TokenExtraBits[token];
      var extra = magnitude - Vp9Tables.TokenBaseValue[token];

      var probabilities = category == 6
        ? Vp9Tables.Category6Probabilities[^bits..]
        : Vp9Tables.CategoryProbabilities.Slice(category * 14, bits);

      for (var bit = bits - 1; bit >= 0; --bit)
        writer.WriteBool(probabilities[bits - 1 - bit], (extra >> bit) & 1);
    }
  }

  // ============================================================================================
  // Plain-bit header writer
  // ============================================================================================

  private sealed class BitWriter {
    private readonly System.Collections.Generic.List<byte> _bytes = [];
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

    internal byte[] Finish() {
      while (this._count != 0)
        this.Literal(1, 0);
      return this._bytes.ToArray();
    }
  }
}
