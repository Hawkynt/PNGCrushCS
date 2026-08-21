using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Decodes a VP8 stream, one coded frame at a time, keeping the reference frames and the entropy
/// state a later frame needs (RFC 6386).
/// </summary>
/// <remarks>
/// The order of work within a frame is fixed by the format and not by convenience. The whole frame's
/// prediction records are read from the first partition; then every macroblock is reconstructed,
/// reading its residue from whichever token partition its row belongs to; and only when the last
/// macroblock is built does the loop filter run. That last part is not an optimisation — intra
/// prediction reads the reconstruction and must not see filtered samples, and the filtered frame is
/// what the next frame predicts from.
/// </remarks>
internal sealed class Vp8Decoder {

  private const int _KEY_FRAME_HEADER_SIZE = 7;
  private const int _MAX_TOKEN_PARTITIONS = 8;

  private readonly Vp8Segmentation _segmentation = new();
  private readonly Vp8LoopFilterHeader _loopFilter = new();
  private readonly Vp8Quantiser _quantiser = new();
  private readonly Vp8Entropy _entropy = new();
  private readonly int[] _signBias = new int[Vp8Reference.COUNT];
  private readonly Vp8BoolDecoder[] _tokenPartitions = new Vp8BoolDecoder[_MAX_TOKEN_PARTITIONS];
  private readonly List<Vp8Frame> _pool = [];

  private byte[] _packet = [];
  private Vp8MacroblockGrid? _grid;
  private Vp8Frame? _last;
  private Vp8Frame? _golden;
  private Vp8Frame? _alternate;

  private byte[] _aboveContexts = [];
  private readonly byte[] _leftContexts = new byte[9];
  private readonly short[] _coefficients = new short[25 * 16];

  private int _macroblockColumns;
  private int _macroblockRows;
  private int _partitionCount = 1;

  /// <summary>The picture size the last key frame stated, which is what a decoded frame is cropped to.</summary>
  internal int Width { get; private set; }

  internal int Height { get; private set; }

  /// <summary>
  /// Decodes one coded frame.
  /// </summary>
  /// <param name="data">The packet, which for VP8 is exactly one frame.</param>
  /// <param name="frame">The reconstructed picture, whether or not it is meant to be shown.</param>
  /// <returns><c>true</c> when the frame is one to display, which a hidden reference frame is not.</returns>
  internal bool Decode(ReadOnlySpan<byte> data, out Vp8Frame frame) {
    if (data.Length < 3)
      throw new InvalidDataException(
        $"A VP8 frame is at least the three bytes of its frame tag; this one is {data.Length}.");

    if (this._packet.Length < data.Length)
      this._packet = new byte[data.Length];

    data.CopyTo(this._packet);
    var packet = this._packet;

    var tag = packet[0] | (packet[1] << 8) | (packet[2] << 16);
    var isKeyFrame = (tag & 1) == 0;
    var version = (tag >> 1) & 7;
    var showFrame = ((tag >> 4) & 1) != 0;
    var firstPartitionSize = (tag >> 5) & 0x7FFFF;

    if (version > 3)
      throw new NotSupportedException(
        $"This VP8 frame states version {version}. RFC 6386 section 9.1 defines versions 0 to 3 and reserves the "
        + "rest for future variants of the format, which this decoder does not implement.");

    var at = 3;
    if (isKeyFrame)
      at = this._ReadKeyFrameHeader(packet, data.Length);

    if (at + firstPartitionSize > data.Length)
      throw new InvalidDataException(
        $"This VP8 frame states a first partition of {firstPartitionSize} byte(s), which does not fit in the "
        + $"{data.Length - at} that follow its header. The packet is truncated.");

    // The first partition carries the whole frame header, so unlike a token partition it cannot
    // credibly be one byte: a frame with nothing to say still has several dozen bits to say it in.
    if (firstPartitionSize < 2)
      throw new InvalidDataException(
        $"This VP8 frame states a first partition of {firstPartitionSize} byte(s). That partition holds the frame "
        + "header of RFC 6386 section 9, which cannot fit in it.");

    if (this._grid == null)
      throw new InvalidDataException(
        "This VP8 stream begins with an interframe. An interframe is a difference from the frames before it, and "
        + "there are none; decoding has to start at a key frame.");

    var header = new Vp8BoolDecoder(packet, at, firstPartitionSize);

    if (isKeyFrame) {
      // The colour space and the clamping type. Both are defined as zero in every version of the
      // format that exists, and RFC 6386 section 9.2 reserves the other values rather than giving
      // them a meaning, so a frame that sets either is a frame this cannot claim to have decoded.
      if (header.ReadLiteral(2) != 0)
        throw new NotSupportedException(
          "This VP8 key frame sets the colour space or clamping type field, which RFC 6386 section 9.2 reserves. "
          + "A stream using either is not implemented.");

      this._segmentation.Reset();
      this._loopFilter.Reset();
    }

    this._segmentation.Parse(ref header);
    this._loopFilter.Parse(ref header);

    var partitionCount = 1 << header.ReadLiteral(2);
    this._SetUpTokenPartitions(packet, at + firstPartitionSize, data.Length - at - firstPartitionSize, partitionCount);

    this._quantiser.Parse(ref header);

    var refreshGolden = isKeyFrame || header.ReadFlag() != 0;
    var refreshAlternate = isKeyFrame || header.ReadFlag() != 0;
    var copyToGolden = !isKeyFrame && !refreshGolden ? header.ReadLiteral(2) : 0;
    var copyToAlternate = !isKeyFrame && !refreshAlternate ? header.ReadLiteral(2) : 0;

    if (!isKeyFrame) {
      this._signBias[Vp8Reference.GOLDEN] = header.ReadFlag();
      this._signBias[Vp8Reference.ALTERNATE] = header.ReadFlag();
    } else {
      this._signBias[Vp8Reference.GOLDEN] = 0;
      this._signBias[Vp8Reference.ALTERNATE] = 0;
    }

    var refreshEntropy = header.ReadFlag() != 0;
    var refreshLast = isKeyFrame || header.ReadFlag() != 0;

    // A key frame puts every probability back to its default, and it does so whether or not this
    // frame means to keep what it is about to read on top of them.
    if (isKeyFrame)
      this._entropy.Reset();

    if (!refreshEntropy)
      this._entropy.Save();

    this._entropy.ParseUpdates(
      ref header, isKeyFrame,
      out var skipEnabled, out var skipProbability,
      out var intraProbability, out var lastProbability, out var goldenProbability);

    this._quantiser.Build(this._segmentation);

    var grid = this._grid;
    this._segmentation.Resize(grid.Columns * grid.Rows);

    Vp8ModeReader.ReadFrame(
      ref header, grid, this._segmentation, this._entropy, isKeyFrame,
      skipEnabled, skipProbability, intraProbability, lastProbability, goldenProbability, this._signBias);

    var current = this._TakeFreeFrame();
    this._Reconstruct(current, grid, version);
    Vp8LoopFilter.Apply(current, grid, this._segmentation, this._loopFilter, isKeyFrame);

    if (!refreshEntropy)
      this._entropy.Restore();

    this._UpdateReferences(current, refreshLast, refreshGolden, refreshAlternate, copyToGolden, copyToAlternate);

    frame = current;
    return showFrame;
  }

  // ============================================================================================
  // Frame header
  // ============================================================================================

  /// <summary>Reads the seven bytes only a key frame carries, and resizes everything if the picture changed.</summary>
  private int _ReadKeyFrameHeader(byte[] packet, int length) {
    if (length < 3 + _KEY_FRAME_HEADER_SIZE)
      throw new InvalidDataException(
        $"A VP8 key frame carries a further {_KEY_FRAME_HEADER_SIZE} bytes of start code and picture size after its "
        + $"frame tag; this packet holds only {length}.");

    if (packet[3] != 0x9D || packet[4] != 0x01 || packet[5] != 0x2A)
      throw new InvalidDataException(
        $"This VP8 key frame does not carry the start code 9D 01 2A that RFC 6386 section 9.1 requires, but "
        + $"{packet[3]:X2} {packet[4]:X2} {packet[5]:X2}.");

    var width = (packet[6] | (packet[7] << 8)) & 0x3FFF;
    var height = (packet[8] | (packet[9] << 8)) & 0x3FFF;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"This VP8 key frame states a picture of {width}x{height}, which has no samples.");

    // The upper two bits of each field are a scale the writer would like the picture shown at. They
    // change no sample of the decode and are not applied here, for the same reason no other decoder
    // applies them: a decoder that resampled would hand back a picture that is in no frame.
    if (width == this.Width && height == this.Height)
      return 3 + _KEY_FRAME_HEADER_SIZE;

    this.Width = width;
    this.Height = height;
    this._macroblockColumns = (width + 15) / 16;
    this._macroblockRows = (height + 15) / 16;
    this._grid = new(this._macroblockColumns, this._macroblockRows);
    this._aboveContexts = new byte[this._macroblockColumns * 9];
    this._pool.Clear();
    this._last = this._golden = this._alternate = null;

    return 3 + _KEY_FRAME_HEADER_SIZE;
  }

  /// <summary>
  /// Finds the token partitions, whose sizes sit in a small table right after the first partition
  /// (RFC 6386, 9.5).
  /// </summary>
  private void _SetUpTokenPartitions(byte[] packet, int at, int available, int count) {
    var tableSize = 3 * (count - 1);
    if (available < tableSize)
      throw new InvalidDataException(
        $"This VP8 frame states {count} token partitions, whose size table needs {tableSize} bytes, and only "
        + $"{available} remain after the first partition.");

    var table = at;
    var data = at + tableSize;
    var remaining = available - tableSize;

    for (var partition = 0; partition < count; ++partition) {
      var size = partition < count - 1
        ? packet[table] | (packet[table + 1] << 8) | (packet[table + 2] << 16)
        : remaining;

      if (size > remaining)
        throw new InvalidDataException(
          $"This VP8 frame states a token partition {partition} of {size} byte(s) where only {remaining} remain. "
          + "The packet is truncated.");

      this._tokenPartitions[partition] = new(packet, data, size);
      table += 3;
      data += size;
      remaining -= size;
    }

    this._partitionCount = count;
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  private void _Reconstruct(Vp8Frame current, Vp8MacroblockGrid grid, int version) {
    Array.Clear(this._aboveContexts);

    var bicubic = version == 0;
    var wholePixelsOnly = version == 3;
    var coefficients = this._coefficients.AsSpan();

    for (var row = 0; row < grid.Rows; ++row) {
      Array.Clear(this._leftContexts);
      ref var tokens = ref this._tokenPartitions[row % this._partitionCount];

      for (var column = 0; column < grid.Columns; ++column) {
        var index = grid.IndexOf(row, column);
        var lumaMode = grid.LumaMode[index];
        var isIntra = grid.ReferenceFrame[index] == Vp8Reference.CURRENT;
        var hasY2 = lumaMode != Vp8Mode.SUBBLOCK_PREDICTION && lumaMode != Vp8Mode.SPLIT_MV;
        var above = this._aboveContexts.AsSpan(column * 9, 9);

        int residueMask;
        if (grid.Skipped[index]) {
          Vp8TokenReader.SkipMacroblock(this._leftContexts, above, hasY2);
          coefficients.Clear();
          residueMask = 0;
        } else {
          residueMask = Vp8TokenReader.ReadMacroblock(
            ref tokens, this._entropy.CoefficientProbabilities, this._quantiser, grid.Segment[index],
            hasY2, this._leftContexts, above, coefficients);

          if (hasY2)
            Vp8Transform.InvertWalshHadamard(coefficients);
        }

        grid.HasResidue[index] = (residueMask & Vp8TokenReader.ANY_RESIDUE) != 0;

        if (isIntra)
          _PredictIntra(current, grid, index, row, column, lumaMode, coefficients, residueMask);
        else
          this._PredictInter(current, grid, index, row, column, lumaMode, bicubic, wholePixelsOnly);

        if (!isIntra || lumaMode != Vp8Mode.SUBBLOCK_PREDICTION)
          _AddLumaResidue(current, row, column, coefficients, residueMask);

        _AddChromaResidue(current, row, column, coefficients, residueMask);
      }
    }
  }

  private static void _AddLumaResidue(
    Vp8Frame frame, int row, int column, ReadOnlySpan<short> coefficients, int residueMask) {
    var stride = frame.LumaWidth;
    var origin = row * 16 * stride + column * 16;

    for (var block = 0; block < 16; ++block) {
      var at = block * 16;
      if ((residueMask & (1 << block)) == 0 && coefficients[at] == 0)
        continue;

      Vp8Transform.AddResidue(
        coefficients.Slice(at, 16), frame.Luma,
        origin + (block >> 2) * 4 * stride + (block & 3) * 4, stride);
    }
  }

  private static void _AddChromaResidue(
    Vp8Frame frame, int row, int column, ReadOnlySpan<short> coefficients, int residueMask) {
    var stride = frame.ChromaWidth;
    var origin = row * 8 * stride + column * 8;

    for (var block = 16; block < 24; ++block) {
      var at = block * 16;
      if ((residueMask & (1 << block)) == 0 && coefficients[at] == 0)
        continue;

      var plane = block < 20 ? frame.Cb : frame.Cr;
      var within = block & 3;
      Vp8Transform.AddResidue(
        coefficients.Slice(at, 16), plane,
        origin + (within >> 1) * 4 * stride + (within & 1) * 4, stride);
    }
  }

  // ============================================================================================
  // Intra-coded macroblocks
  // ============================================================================================

  private static void _PredictIntra(
    Vp8Frame frame, Vp8MacroblockGrid grid, int index, int row, int column, int lumaMode,
    ReadOnlySpan<short> coefficients, int residueMask) {
    var lumaStride = frame.LumaWidth;
    var chromaStride = frame.ChromaWidth;
    var x = column * 16;
    var y = row * 16;

    Span<byte> edge = stackalloc byte[21];
    Span<byte> left = stackalloc byte[16];

    Vp8IntraPrediction.GatherAbove(frame.Luma, lumaStride, frame.LumaWidth, x, y, 16, 4, edge);
    Vp8IntraPrediction.GatherLeft(frame.Luma, lumaStride, x, y, 16, left);

    if (lumaMode == Vp8Mode.SUBBLOCK_PREDICTION)
      _PredictSubblocks(frame, grid, index, x, y, edge, left, coefficients, residueMask);
    else
      Vp8IntraPrediction.PredictBlock(
        frame.Luma, lumaStride, x, y, 16, lumaMode, edge, left, row > 0, column > 0);

    var chromaX = column * 8;
    var chromaY = row * 8;
    var chromaMode = grid.ChromaMode[index];

    Vp8IntraPrediction.GatherAbove(frame.Cb, chromaStride, frame.ChromaWidth, chromaX, chromaY, 8, 0, edge);
    Vp8IntraPrediction.GatherLeft(frame.Cb, chromaStride, chromaX, chromaY, 8, left);
    Vp8IntraPrediction.PredictBlock(
      frame.Cb, chromaStride, chromaX, chromaY, 8, chromaMode, edge, left, row > 0, column > 0);

    Vp8IntraPrediction.GatherAbove(frame.Cr, chromaStride, frame.ChromaWidth, chromaX, chromaY, 8, 0, edge);
    Vp8IntraPrediction.GatherLeft(frame.Cr, chromaStride, chromaX, chromaY, 8, left);
    Vp8IntraPrediction.PredictBlock(
      frame.Cr, chromaStride, chromaX, chromaY, 8, chromaMode, edge, left, row > 0, column > 0);
  }

  /// <summary>
  /// Predicts and reconstructs the sixteen luma subblocks of a B_PRED macroblock, one at a time.
  /// </summary>
  /// <remarks>
  /// One at a time and in order, because each subblock predicts from the reconstruction of the ones
  /// before it — so the residue has to be added to a subblock before the next one is predicted.
  /// <para/>
  /// The four samples above and to the right of a subblock are the exception to that. For the
  /// subblocks down the right-hand edge they have not been reconstructed yet, and RFC 6386 section
  /// 12.3 says to use the four above and to the right of the macroblock instead — which is why the
  /// row above is gathered once, before any of this, and kept.
  /// </remarks>
  private static void _PredictSubblocks(
    Vp8Frame frame, Vp8MacroblockGrid grid, int index, int x, int y,
    ReadOnlySpan<byte> macroblockAbove, ReadOnlySpan<byte> macroblockLeft,
    ReadOnlySpan<short> coefficients, int residueMask) {
    var stride = frame.LumaWidth;
    var plane = frame.Luma;
    var modes = grid.SubblockModes(index);

    Span<byte> above = stackalloc byte[8];
    Span<byte> left = stackalloc byte[4];
    Span<byte> edge = stackalloc byte[9];

    for (var block = 0; block < 16; ++block) {
      var subRow = block >> 2;
      var subColumn = block & 3;
      var blockX = x + subColumn * 4;
      var blockY = y + subRow * 4;
      var aboveRow = (blockY - 1) * stride;

      if (subRow == 0)
        macroblockAbove.Slice(1 + subColumn * 4, 8).CopyTo(above);
      else {
        for (var i = 0; i < 4; ++i)
          above[i] = plane[aboveRow + blockX + i];

        if (subColumn < 3)
          for (var i = 0; i < 4; ++i)
            above[4 + i] = plane[aboveRow + blockX + 4 + i];
        else
          macroblockAbove.Slice(17, 4).CopyTo(above[4..]);
      }

      if (subColumn == 0)
        macroblockLeft.Slice(subRow * 4, 4).CopyTo(left);
      else
        for (var i = 0; i < 4; ++i)
          left[i] = plane[(blockY + i) * stride + blockX - 1];

      var corner = subRow == 0
        ? macroblockAbove[subColumn * 4]
        : subColumn == 0
          ? macroblockLeft[subRow * 4 - 1]
          : plane[aboveRow + blockX - 1];

      edge[0] = left[3];
      edge[1] = left[2];
      edge[2] = left[1];
      edge[3] = left[0];
      edge[4] = corner;
      above[..4].CopyTo(edge[5..]);

      Vp8IntraPrediction.PredictSubblock(plane, stride, blockX, blockY, modes[block], edge, above, left);

      var at = block * 16;
      if ((residueMask & (1 << block)) != 0 || coefficients[at] != 0)
        Vp8Transform.AddResidue(coefficients.Slice(at, 16), plane, blockY * stride + blockX, stride);
    }
  }

  // ============================================================================================
  // Inter-coded macroblocks
  // ============================================================================================

  private void _PredictInter(
    Vp8Frame frame, Vp8MacroblockGrid grid, int index, int row, int column, int lumaMode,
    bool bicubic, bool wholePixelsOnly) {
    var reference = grid.ReferenceFrame[index] switch {
      Vp8Reference.LAST => this._last,
      Vp8Reference.GOLDEN => this._golden,
      _ => this._alternate,
    } ?? throw new InvalidDataException(
      "A VP8 macroblock predicts from a reference frame this stream has never coded. Decoding has to start at a "
      + "key frame, which codes all three.");

    var lumaStride = frame.LumaWidth;
    var chromaStride = frame.ChromaWidth;
    var x = column * 16;
    var y = row * 16;
    var chromaX = column * 8;
    var chromaY = row * 8;

    var isSplit = lumaMode == Vp8Mode.SPLIT_MV;
    var motionVectors = grid.SubblockMotionVectors(index);
    var wholeBlock = grid.MotionVector[index];

    for (var block = 0; block < 16; ++block)
      Vp8InterPrediction.PredictBlock(
        reference.Luma, frame.Luma, lumaStride, frame.LumaHeight,
        x + (block & 3) * 4, y + (block >> 2) * 4,
        isSplit ? motionVectors[block] : wholeBlock, bicubic);

    Span<int> quarterOrigins = [0, 2, 8, 10];
    for (var quarter = 0; quarter < 4; ++quarter) {
      var chromaVector = isSplit
        ? Vp8InterPrediction.ChromaVector(motionVectors, quarterOrigins[quarter], wholePixelsOnly)
        : Vp8InterPrediction.ChromaVector(wholeBlock, wholePixelsOnly);

      var blockX = chromaX + (quarter & 1) * 4;
      var blockY = chromaY + (quarter >> 1) * 4;

      Vp8InterPrediction.PredictBlock(
        reference.Cb, frame.Cb, chromaStride, frame.ChromaHeight, blockX, blockY, chromaVector, bicubic);
      Vp8InterPrediction.PredictBlock(
        reference.Cr, frame.Cr, chromaStride, frame.ChromaHeight, blockX, blockY, chromaVector, bicubic);
    }
  }

  // ============================================================================================
  // Reference frames
  // ============================================================================================

  /// <summary>
  /// Applies the reference frame updates a frame header asked for (RFC 6386, 9.7 and 9.8).
  /// </summary>
  /// <remarks>
  /// Every one of them reads the state as it was before this frame. A header can say both "copy the
  /// altref into the golden" and "copy the golden into the altref" at once, and taking them in
  /// sequence would make the second see the first's result and the two would swap or not depending
  /// on which order a decoder happened to pick. Taking a snapshot removes the question.
  /// </remarks>
  private void _UpdateReferences(
    Vp8Frame current, bool refreshLast, bool refreshGolden, bool refreshAlternate,
    int copyToGolden, int copyToAlternate) {
    var last = this._last;
    var golden = this._golden;
    var alternate = this._alternate;

    this._golden = refreshGolden ? current : copyToGolden switch { 1 => last, 2 => alternate, _ => golden };
    this._alternate = refreshAlternate ? current : copyToAlternate switch { 1 => last, 2 => golden, _ => alternate };
    this._last = refreshLast ? current : last;
  }

  /// <summary>A frame buffer no reference is holding, so that decoding into it cannot spoil one.</summary>
  private Vp8Frame _TakeFreeFrame() {
    foreach (var candidate in this._pool)
      if (candidate != this._last && candidate != this._golden && candidate != this._alternate)
        return candidate;

    var frame = new Vp8Frame(this._macroblockColumns, this._macroblockRows);
    this._pool.Add(frame);
    return frame;
  }
}
