using System;
using System.Collections.Generic;
using System.IO;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Decodes a VP9 stream: the superframe index, the frame headers, the eight reference slots and the
/// probability state that carries from one frame to the next.
/// </summary>
/// <remarks>
/// A packet is not a frame. VP9 packs several coded frames into one chunk — a superframe — with an
/// index of their sizes in its last few bytes, and most of the frames in it are usually not meant to
/// be shown: an alternate reference frame built from several source frames at once is decoded, stored
/// and never displayed. That is why decoding a packet answers with a list rather than a picture, and
/// why the list is sometimes empty.
/// <para/>
/// The probability state is the part that has to be got exactly right and cannot be checked from one
/// frame alone. Four saved sets of tables live here; a frame says which to start from, the compressed
/// header sends changes on top, and at the end of the frame the tables are moved towards the
/// frequencies the frame turned out to have and possibly written back. Get any of that wrong and the
/// frame still decodes perfectly — it is the frame after it that becomes noise.
/// </remarks>
internal sealed class Vp9Decoder {

  private readonly Vp9FrameHeader _header = new();
  private readonly Vp9Probabilities _probabilities = new();
  private readonly Vp9Probabilities[] _savedProbabilities = new Vp9Probabilities[FRAME_CONTEXTS];
  private readonly Vp9Counts _counts = new();
  private readonly Vp9LoopFilter _loopFilter = new();
  private readonly Vp9FrameDecoder _frameDecoder;

  private readonly Vp9Frame?[] _slots = new Vp9Frame?[NUM_REF_FRAMES];
  private readonly int[] _slotWidths = new int[NUM_REF_FRAMES];
  private readonly int[] _slotHeights = new int[NUM_REF_FRAMES];
  private readonly bool[] _slotIsValid = new bool[NUM_REF_FRAMES];
  private readonly List<Vp9Frame> _pool = [];

  private Vp9ModeInfoGrid? _grid;
  private byte[] _packet = [];

  internal Vp9Decoder() {
    for (var i = 0; i < FRAME_CONTEXTS; ++i)
      this._savedProbabilities[i] = new();

    this._frameDecoder = new(this._header, this._probabilities, this._counts);
  }

  /// <summary>What the frame states the picture size is, which is what a decoded frame is cropped to.</summary>
  internal int Width => this._header.FrameWidth;

  internal int Height => this._header.FrameHeight;

  internal int ColorRange => this._header.ColorRange;

  /// <summary>
  /// Decodes one packet and answers the pictures it is meant to show, in order.
  /// </summary>
  /// <remarks>
  /// Usually exactly one. A chunk of hidden reference frames answers with none, and a chunk that
  /// carries several shown frames answers with all of them.
  /// </remarks>
  internal IReadOnlyList<Vp9Frame> Decode(ReadOnlySpan<byte> data) {
    if (data.Length < 1)
      throw new InvalidDataException("A VP9 packet cannot be empty.");

    if (this._packet.Length < data.Length)
      this._packet = new byte[data.Length];

    data.CopyTo(this._packet);

    var shown = new List<Vp9Frame>();
    Span<int> sizes = stackalloc int[8];
    var count = _ReadSuperframeIndex(this._packet, data.Length, sizes);

    if (count == 0) {
      this._DecodeFrame(0, data.Length, shown);
      return shown;
    }

    var at = 0;
    for (var i = 0; i < count; ++i) {
      this._DecodeFrame(at, sizes[i], shown);
      at += sizes[i];
    }

    return shown;
  }

  // ============================================================================================
  // Superframes (specification Annex B)
  // ============================================================================================

  /// <summary>
  /// Reads the index a superframe carries in its last bytes, if it carries one.
  /// </summary>
  /// <returns>How many frames the chunk holds, or zero when it is a single frame.</returns>
  /// <remarks>
  /// Recognised by a marker byte that appears twice, once at each end of the index. A coded frame is
  /// required never to end in a byte that looks like the marker, so the two tests together cannot be
  /// passed by accident.
  /// <para/>
  /// The sizes are little-endian, which the specification's syntax table does not quite say: it gives
  /// the descriptor as <c>f(SzBytes)</c>, and <c>f</c> counts bits. Bytes are meant, in the order
  /// every writer of the format has used.
  /// </remarks>
  private static int _ReadSuperframeIndex(byte[] data, int length, Span<int> sizes) {
    if (length < 2)
      return 0;

    var marker = data[length - 1];
    if ((marker & 0xE0) != 0xC0)
      return 0;

    var bytesPerSize = ((marker >> 3) & 3) + 1;
    var frames = (marker & 7) + 1;
    var indexSize = 2 + frames * bytesPerSize;

    if (length < indexSize || data[length - indexSize] != marker)
      return 0;

    var at = length - indexSize + 1;
    var total = 0;

    for (var i = 0; i < frames; ++i) {
      var size = 0;
      for (var b = 0; b < bytesPerSize; ++b)
        size |= data[at + b] << (8 * b);

      at += bytesPerSize;
      sizes[i] = size;
      total += size;

      if (size <= 0 || total > length - indexSize)
        throw new InvalidDataException(
          $"This VP9 superframe states {frames} frames whose sizes come to more than the {length - indexSize} bytes "
          + "before its index. The packet is truncated or the index is not one.");
    }

    return frames;
  }

  // ============================================================================================
  // One frame (specification 6.1)
  // ============================================================================================

  private void _DecodeFrame(int at, int size, List<Vp9Frame> shown) {
    if (size < 1)
      throw new InvalidDataException("A VP9 frame cannot be empty.");

    var reader = new Vp9BitReader(this._packet, at, size);
    this._header.Parse(ref reader, this._slotWidths, this._slotHeights, this._slotIsValid);

    if (this._header.ShowExistingFrame) {
      shown.Add(
        this._slots[this._header.FrameToShowMapIndex]
        ?? throw new InvalidDataException(
          $"This VP9 frame asks for reference slot {this._header.FrameToShowMapIndex} to be shown, and no frame of "
          + "this stream has written it."));

      return;
    }

    reader.AlignToByte();

    if (this._header.NeedsPastIndependence) {
      this._probabilities.Reset();

      if (this._header.ResetsAllFrameContexts)
        foreach (var saved in this._savedProbabilities)
          this._probabilities.SaveTo(saved);
      else if (this._header.ResetsOneFrameContext)
        this._probabilities.SaveTo(this._savedProbabilities[this._header.ContextIndexToReset]);
    }

    // Checked before anything is sized to the picture the header claims, so that a packet too short
    // to be the frame it says it is fails on its own length rather than on however much memory the
    // stated picture size would ask for.
    var uncompressed = reader.BytePosition - at;
    if (uncompressed + this._header.HeaderSizeInBytes > size)
      throw new InvalidDataException(
        $"This VP9 frame states a compressed header of {this._header.HeaderSizeInBytes} byte(s), which does not fit "
        + $"in the {size - uncompressed} that follow its uncompressed header. The packet is truncated.");

    var grid = this._PrepareGrid();

    this._probabilities.LoadFrom(this._savedProbabilities[this._header.FrameContextIndex]);
    this._probabilities.LoadTransformSizeAndSkipFrom(this._savedProbabilities[this._header.FrameContextIndex]);
    this._counts.Clear();

    var compressed = new Vp9BoolDecoder(this._packet, at + uncompressed, this._header.HeaderSizeInBytes);
    compressed.ReadMarker();
    Vp9CompressedHeader.Parse(ref compressed, this._header, this._probabilities);

    var tilesAt = at + uncompressed + this._header.HeaderSizeInBytes;
    var tilesLength = size - uncompressed - this._header.HeaderSizeInBytes;

    var current = this._TakeFreeFrame(shown);
    this._frameDecoder.DecodeTiles(this._packet, tilesAt, tilesLength, current, this._slots);
    this._loopFilter.Apply(current, grid, this._header);

    this._RefreshProbabilities();

    if (this._header.SegmentationEnabled && this._header.SegmentationUpdateMap)
      grid.KeepSegmentMap();

    if (this._header.ShowFrame)
      shown.Add(current);

    this._UpdateReferences(current);
    grid.KeepForNextFrame();
  }

  /// <summary>
  /// Builds or reuses the mode info grid, and forgets what it held when the frame says to.
  /// </summary>
  private Vp9ModeInfoGrid _PrepareGrid() {
    var grid = this._grid;

    if (grid == null || grid.Columns != this._header.Sb64Cols * 8 || grid.Rows != this._header.Sb64Rows * 8) {
      grid = new(this._header.Sb64Cols, this._header.Sb64Rows);
      this._grid = grid;
      this._frameDecoder.Resize(grid);
    } else if (this._header.SizeChanged || this._header.NeedsPastIndependence)
      grid.ClearSegmentMap();

    return grid;
  }

  /// <summary>
  /// Moves the probability tables towards what this frame turned out to be, and saves them if the
  /// frame asked for it (specification 6.1.2).
  /// </summary>
  /// <remarks>
  /// The adaptation starts from the tables as they were saved, not as they were used. A frame's
  /// forward updates are its own; what the next frame inherits is the saved set moved by the
  /// frequencies, which is why the saved set is loaded back over the working one first.
  /// </remarks>
  private void _RefreshProbabilities() {
    var saved = this._savedProbabilities[this._header.FrameContextIndex];

    if (!this._header.ErrorResilientMode && !this._header.FrameParallelDecodingMode) {
      this._probabilities.LoadFrom(saved);

      // A frame that follows a key frame inherited the format's defaults rather than anything
      // measured, so it is allowed to move further towards what it saw.
      var updateFactor = !this._header.FrameIsIntra && this._header.LastFrameType == KEY_FRAME ? 128 : 112;
      Vp9Adaptation.AdaptCoefficients(this._probabilities, this._counts, updateFactor);

      if (!this._header.FrameIsIntra) {
        this._probabilities.LoadTransformSizeAndSkipFrom(saved);
        Vp9Adaptation.AdaptNonCoefficients(
          this._probabilities, this._counts,
          this._header.InterpolationFilter == SWITCHABLE,
          this._header.TransformMode == TX_MODE_SELECT,
          this._header.AllowHighPrecisionMotionVectors);
      }
    }

    if (this._header.RefreshFrameContext)
      this._probabilities.SaveTo(saved);
  }

  // ============================================================================================
  // Reference frames (specification 8.10)
  // ============================================================================================

  private void _UpdateReferences(Vp9Frame current) {
    for (var i = 0; i < NUM_REF_FRAMES; ++i) {
      if (((this._header.RefreshFrameFlags >> i) & 1) == 0)
        continue;

      this._slots[i] = current;
      this._slotWidths[i] = this._header.FrameWidth;
      this._slotHeights[i] = this._header.FrameHeight;
      this._slotIsValid[i] = true;
    }
  }

  /// <summary>
  /// A frame buffer no reference slot is holding and no picture already decoded from this packet is
  /// waiting in.
  /// </summary>
  private Vp9Frame _TakeFreeFrame(List<Vp9Frame> shown) {
    var width = this._header.FrameWidth;
    var height = this._header.FrameHeight;
    var columns = this._header.Sb64Cols;
    var rows = this._header.Sb64Rows;
    var subX = this._header.SubsamplingX;
    var subY = this._header.SubsamplingY;

    // A slot may hold a picture of a size the stream has moved on from, because a frame is allowed
    // to predict from a reference of a different size. Only the buffers nothing is holding at all are
    // dropped when the size changes. Chroma geometry is part of buffer identity too: a 4:4:4 frame
    // cannot be reconstructed into a recycled 4:2:0 buffer merely because their luma sizes match.
    Vp9Frame? free = null;
    for (var i = this._pool.Count - 1; i >= 0; --i) {
      var candidate = this._pool[i];
      if (Array.IndexOf(this._slots, candidate) >= 0 || shown.Contains(candidate))
        continue;

      if (candidate.Matches(width, height, columns, rows, subX, subY)) {
        free ??= candidate;
        continue;
      }

      this._pool.RemoveAt(i);
    }

    if (free != null)
      return free;

    var frame = new Vp9Frame(
      width, height, columns, rows, subX, subY, this._header.ColorSpace, this._header.ColorRange);
    this._pool.Add(frame);
    return frame;
  }
}
