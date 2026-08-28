using System;
using System.Collections.Generic;
using System.IO;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Decodes a VP9 stream: the superframe index, the frame headers, the eight reference slots and the
/// probability state that carries from one frame to the next.
/// </summary>
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

  internal int Width => this._header.FrameWidth;
  internal int Height => this._header.FrameHeight;
  internal int ColorRange => this._header.ColorRange;

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
    Vp9InterPrediction.ConfigureCurrentFrame(this._header.SubsamplingX, this._header.SubsamplingY);
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

  private void _RefreshProbabilities() {
    var saved = this._savedProbabilities[this._header.FrameContextIndex];

    if (!this._header.ErrorResilientMode && !this._header.FrameParallelDecodingMode) {
      this._probabilities.LoadFrom(saved);

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

  private Vp9Frame _TakeFreeFrame(List<Vp9Frame> shown) {
    var width = this._header.FrameWidth;
    var height = this._header.FrameHeight;
    var columns = this._header.Sb64Cols;
    var rows = this._header.Sb64Rows;
    var subX = this._header.SubsamplingX;
    var subY = this._header.SubsamplingY;

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
