using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;
using static FileFormat.Codecs.TrueMotion1Format;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Duck TrueMotion 1 (<c>DUCK</c>) at sixteen bits (RGB555) or twenty-four, as key frames and
/// change-bit inter frames.
/// </summary>
/// <remarks>
/// FFmpeg decodes TrueMotion 1 and has no encoder, so nothing is translated here: this is an original
/// inverse of the decoder adapted in <see cref="TrueMotion1VideoDecoder"/>, built on the same fixed
/// tables, and FFmpeg's decoder is the oracle that reads what it writes.
/// <para/>
/// <b>The format is lossy by construction.</b> Every delta comes from a fixed vector table, a byte of
/// index stream choosing one group of one to four delta pairs that are then spent on whatever
/// predictor steps come next; luminance steps move all three channels together and chrominance steps
/// move red and blue. A picture is approximated, not stored. The encoder walks exactly the sequence of
/// steps the decoder will take and, wherever the decoder is about to read a byte, tries every byte it
/// could read — and every escape byte pair, which adds a fivefold (sixteen-bit) or fat-table
/// (twenty-four-bit) delta to the step just taken — simulating each until the next byte would be read
/// and keeping the one whose reconstructed pixels are closest to the picture. It predicts from its own
/// reconstruction, never from the source, so the decoder's picture and the encoder's never drift apart.
/// <para/>
/// Sixteen-bit streams use compression type 8 (2x2 chroma blocks), twenty-four-bit ones type 16, in a
/// version 2, type 2 header. Each key frame is coded under all four delta sets and all three vector
/// tables and the closest is kept, so a picture with hard edges gets the coarse deltas and a smooth one
/// the fine. Every twelfth picture is a key
/// frame; between them a four-by-four block (two pixels wide at twenty-four bits) whose source
/// pixels have not changed since the previous picture is marked unchanged and copied. A request
/// stating 24 or 32 bits per pixel selects the twenty-four-bit form; anything else, sixteen.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class TrueMotion1VideoEncoder : IVideoCodecEncoder<TrueMotion1VideoEncoder> {

  private const int _KEYFRAME_INTERVAL = 12;
  private const int _HEADER_SIZE = 14;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("DUCK");

  private enum Op : byte { RowStart, Chroma, Luma, Output, Skip }

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly bool _rgb24;
  private readonly int _wordsPerRow;
  private PredictorTables _tables = new();
  private int _deltaSet;
  private int _vectorTable = 1;
  private readonly uint[] _reconstruction;
  private readonly uint[] _vertical;
  private bool _hasReference;
  private int[]? _previousTarget;
  private int _pictureNumber;

  private TrueMotion1VideoEncoder(MediaStreamInfo requested, bool rgb24) {
    this._width = requested.Width;
    this._height = requested.Height;
    this._rgb24 = rgb24;
    this._wordsPerRow = rgb24 ? this._width : this._width >> 1;
    this._reconstruction = new uint[this._wordsPerRow * this._height];
    this._vertical = new uint[this._wordsPerRow];

    var bitsPerPixel = rgb24 ? 24 : 16;
    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: requested.Width,
      Height: requested.Height,
      Planes: 1,
      BitsPerPixel: (short)bitsPerPixel,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = format,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public static string CodecName => "Duck TrueMotion 1";
  public static CodecTag Codec => _Tag;

  public static TrueMotion1VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Duck TrueMotion 1 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"TrueMotion 1 needs a positive picture size; stream {stream.Index} states {stream.Width}x{stream.Height}.");

    var rgb24 = stream.BitsPerPixel is 24 or 32;
    var widthStep = rgb24 ? 2 : 4;
    if (stream.Width % widthStep != 0 || (stream.Height & 3) != 0)
      throw new NotSupportedException(
        $"TrueMotion 1 codes rows in runs of {widthStep} pixels and rows in groups of four, so a "
        + $"{(rgb24 ? "twenty-four" : "sixteen")}-bit picture must be a multiple of {widthStep} wide and of four high; "
        + $"stream {stream.Index} states {stream.Width}x{stream.Height}.");
    if (stream.Width * (rgb24 ? 2 : 1) > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"A TrueMotion 1 header states its size in sixteen bits; {stream.Width}x{stream.Height} does not fit.");

    return new(stream, rgb24);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"TrueMotion 1 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var target = frame.ToRgb24();
    var keyFrame = !this._hasReference || this._pictureNumber % _KEYFRAME_INTERVAL == 0;
    var payload = keyFrame ? this._EncodeKeyFrame(target) : this._EncodePicture(target, keyFrame: false);
    this._hasReference = true;
    ++this._pictureNumber;

    var header = new Header(
      HeaderSize: _HEADER_SIZE,
      Compression: this._rgb24 ? 16 : 8,
      DeltaSet: this._deltaSet,
      VectorTable: this._vectorTable,
      YSize: this._height,
      XSize: this._rgb24 ? this._width * 2 : this._width,
      Checksum: 0,
      Version: 2,
      HeaderType: 2,
      Flags: keyFrame ? FlagKeyFrame : FlagInterFrame,
      Control: 0);

    var data = new byte[_HEADER_SIZE + payload.Count];
    payload.CopyTo(data, _HEADER_SIZE);
    WriteHeader(data, header, data[_HEADER_SIZE]);

    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: keyFrame);
    return true;
  }

  // ==========================================================================================
  // Picture
  // ==========================================================================================

  /// <summary>
  /// Codes a key frame under every delta set and vector table and keeps the closest; the inter frames
  /// that follow use the same tables.
  /// </summary>
  private List<byte> _EncodeKeyFrame(byte[] rgb) {
    List<byte>? bestPayload = null;
    uint[]? bestReconstruction = null;
    PredictorTables? bestTables = null;
    var bestError = long.MaxValue;
    var (bestDeltaSet, bestVectorTable) = (0, 1);
    var target = this._TargetChannels(rgb);

    for (var deltaSet = 0; deltaSet < 4; ++deltaSet)
    for (var vectorTable = 1; vectorTable <= 3; ++vectorTable) {
      var tables = new PredictorTables();
      tables.SelectDeltas(deltaSet);
      tables.Generate(TrueMotion1Tables.VectorTables[vectorTable - 1], this._rgb24 ? Algorithm.Rgb24 : Algorithm.Rgb16);
      this._tables = tables;

      var payload = this._EncodePicture(rgb, keyFrame: true);
      var error = this._PictureError(target);
      if (error >= bestError)
        continue;

      bestError = error;
      bestPayload = payload;
      bestReconstruction = (uint[])this._reconstruction.Clone();
      bestTables = tables;
      (bestDeltaSet, bestVectorTable) = (deltaSet, vectorTable);
    }

    bestReconstruction!.CopyTo(this._reconstruction, 0);
    this._tables = bestTables!;
    (this._deltaSet, this._vectorTable) = (bestDeltaSet, bestVectorTable);
    this._previousTarget = target;
    return bestPayload!;
  }

  private long _PictureError(int[] target) {
    long total = 0;
    for (var row = 0; row < this._height; ++row)
      for (var w = 0; w < this._wordsPerRow; ++w)
        total += this._WordError(this._reconstruction[row * this._wordsPerRow + w], target, row, w);
    return total;
  }

  private List<byte> _EncodePicture(byte[] rgb, bool keyFrame) {
    var target = this._TargetChannels(rgb);
    var payload = new List<byte>();

    // Inter frames: one change bit per run and row group; a set bit copies the run from the previous
    // picture. A run is kept when no coded change could bring it closer than it already is.
    bool[]? changed = null;
    if (!keyFrame) {
      var runsPerRow = this._wordsPerRow / 2;
      var changeRowSize = (runsPerRow + 7) >> 3;
      changed = new bool[runsPerRow * (this._height >> 2)];
      for (var group = 0; group < this._height >> 2; ++group) {
        var bits = new byte[changeRowSize];
        for (var run = 0; run < runsPerRow; ++run) {
          var unchanged = this._RunIsAlreadyBest(target, group, run);
          changed[group * runsPerRow + run] = !unchanged;
          if (unchanged)
            bits[run >> 3] |= (byte)(1 << (run & 7));
        }
        payload.AddRange(bits);
      }
    }

    this._previousTarget = target;
    var ops = this._Schedule(changed);
    var search = new Search(this, target, ops, payload);
    search.Run();
    return payload;
  }

  /// <summary>Every 8-bit channel of the source, in the order a word's pixels lay them out.</summary>
  private int[] _TargetChannels(byte[] rgb) => rgb.Length == 0 ? [] : Array.ConvertAll(rgb, static b => (int)b);

  private bool _RunIsAlreadyBest(int[] target, int group, int run) {
    // A run whose source has not changed since the previous picture keeps its reconstruction; coding
    // it again could not bring it closer than the search already did.
    var previous = this._previousTarget!;
    var pixelsPerWord = this._rgb24 ? 1 : 2;
    for (var row = group * 4; row < group * 4 + 4; ++row) {
      var from = (row * this._width + run * 2 * pixelsPerWord) * 3;
      var to = from + 2 * pixelsPerWord * 3;
      for (var i = from; i < to; ++i)
        if (target[i] != previous[i])
          return false;
    }
    return true;
  }

  /// <summary>The decoder's walk over the picture, step by step: FFmpeg's <c>truemotion1_decode_*bit</c> loop.</summary>
  private (Op Op, int Word)[] _Schedule(bool[]? changed) {
    var ops = new List<(Op, int)>(this._wordsPerRow * this._height * 3);
    var runsPerRow = this._wordsPerRow / 2;
    for (var y = 0; y < this._height; ++y) {
      ops.Add((Op.RowStart, y * this._wordsPerRow));
      for (var run = 0; run < runsPerRow; ++run) {
        var first = y * this._wordsPerRow + run * 2;
        if (changed != null && !changed[(y >> 2) * runsPerRow + run]) {
          ops.Add((Op.Skip, first));
          continue;
        }

        // Compression types 8 and 16 are 2x2 blocks: chrominance before each word on rows 0 and 2.
        var chroma = (y & 3) is 0 or 2;
        for (var w = 0; w < 2; ++w) {
          if (chroma)
            ops.Add((Op.Chroma, first + w));
          ops.Add((Op.Luma, first + w));
          ops.Add((Op.Output, first + w));
        }
      }
    }
    return ops.ToArray();
  }

  /// <summary>Squared error of one reconstructed word against the source, in 8-bit units.</summary>
  private long _WordError(uint word, int[] target, int row, int w) {
    if (this._rgb24) {
      var at = (row * this._width + w) * 3;
      long dr = (int)((word >> 16) & 0xff) - target[at];
      long dg = (int)((word >> 8) & 0xff) - target[at + 1];
      long db = (int)(word & 0xff) - target[at + 2];
      return dr * dr + dg * dg + db * db;
    }

    var p = (row * this._width + w * 2) * 3;
    return _Pixel555Error((ushort)word, target, p) + _Pixel555Error((ushort)(word >> 16), target, p + 3);
  }

  private static readonly int[] _Expand5 = _BuildExpand5();

  private static int[] _BuildExpand5() {
    var table = new int[32];
    for (var i = 0; i < 32; ++i)
      table[i] = (i << 3) | (i >> 2);
    return table;
  }

  private static long _Pixel555Error(ushort pixel, int[] target, int at) {
    // Bit 15 is not part of the picture; a delta that carries into it has already spoiled red.
    long dr = _Expand5[(pixel >> 10) & 31] - target[at];
    long dg = _Expand5[(pixel >> 5) & 31] - target[at + 1];
    long db = _Expand5[pixel & 31] - target[at + 2];
    return dr * dr + dg * dg + db * db;
  }

  // ==========================================================================================
  // Index-stream search
  // ==========================================================================================

  /// <summary>
  /// Replays the decoder's walk, choosing every byte it reads by trying them all.
  /// </summary>
  private sealed class Search {
    private const int _OVERLAY = 16;

    private readonly TrueMotion1VideoEncoder _owner;
    private readonly int[] _target;
    private readonly (Op Op, int Word)[] _ops;
    private readonly List<byte> _payload;
    private readonly uint[] _luma;
    private readonly uint[] _chroma;
    private readonly uint[] _escapeLuma;
    private readonly uint[] _escapeChroma;
    private readonly uint _escapeScale;
    private readonly int _wordsPerRow;

    // Committed state.
    private uint _horizontal;
    private int _index;
    private int _at;

    // Overlay for a simulation: words and vertical predictors it has written, without touching the
    // committed arrays.
    private readonly int[] _overlayWord = new int[_OVERLAY];
    private readonly uint[] _overlayValue = new uint[_OVERLAY];
    private readonly int[] _overlayVertical = new int[_OVERLAY];
    private readonly uint[] _overlayVerticalValue = new uint[_OVERLAY];
    private int _overlayCount;
    private int _overlayVerticalCount;

    public Search(TrueMotion1VideoEncoder owner, int[] target, (Op, int)[] ops, List<byte> payload) {
      this._owner = owner;
      this._target = target;
      this._ops = ops;
      this._payload = payload;
      var tables = owner._tables;
      this._luma = tables.Luma;
      this._chroma = tables.Chroma;
      this._escapeLuma = owner._rgb24 ? tables.FatLuma : tables.Luma;
      this._escapeChroma = owner._rgb24 ? tables.FatChroma : tables.Chroma;
      this._escapeScale = owner._rgb24 ? 1u : 5u;
      this._wordsPerRow = owner._wordsPerRow;
    }

    private readonly record struct Choice(int Byte, int Escape) {
      public bool IsEscape => this.Escape >= 0;
    }

    public void Run() {
      Array.Clear(this._owner._vertical);
      this._at = 0;
      this._horizontal = 0;

      // The first byte is read before any step and is never an escape.
      var choice = this._Choose(escapeAllowed: false);
      var needFetch = this._Commit(choice, escapeAllowed: false);
      while (true) {
        if (needFetch.Pending) {
          choice = this._Choose(needFetch.EscapeAllowed);
          needFetch = this._Commit(choice, needFetch.EscapeAllowed);
          continue;
        }

        if (this._at >= this._ops.Length)
          break;

        needFetch = this._Step(commit: true, ref this._horizontal, ref this._index, ref this._at, out _);
      }
    }

    private readonly record struct Fetch(bool Pending, bool EscapeAllowed);

    /// <summary>Applies a chosen byte (or escape pair) for real and emits it.</summary>
    private Fetch _Commit(Choice choice, bool escapeAllowed) {
      if (!choice.IsEscape) {
        this._payload.Add((byte)choice.Byte);
        this._index = choice.Byte * 4;
        return new(false, false);
      }

      this._payload.Add(0);
      this._payload.Add((byte)choice.Escape);
      var fetch = this._ApplyEscape(choice.Escape, ref this._horizontal, ref this._index, this._EscapeTableForLastStep());
      return fetch;
    }

    private Fetch _ApplyEscape(int escape, ref uint horizontal, ref int index, uint[] table) {
      var pair = table[escape * 4];
      horizontal = unchecked(horizontal + (pair >> 1) * this._escapeScale);
      if ((pair & 1) != 0)
        return new(true, false);

      index = escape * 4 + 1;
      return new(false, false);
    }

    /// <summary>The escape adds to the step that ended the group: a chrominance step or a luminance one.</summary>
    private uint[] _EscapeTableForLastStep() {
      for (var i = this._at - 1; i >= 0; --i)
        switch (this._ops[i].Op) {
          case Op.Chroma: return this._escapeChroma;
          case Op.Luma: return this._escapeLuma;
        }
      return this._escapeLuma;
    }

    /// <summary>
    /// One op of the decoder's walk. Returns whether the decoder would now read a byte.
    /// </summary>
    private Fetch _Step(bool commit, ref uint horizontal, ref int index, ref int at, out long error) {
      error = 0;
      var (op, word) = this._ops[at++];
      var v = word % this._wordsPerRow;
      switch (op) {
        case Op.RowStart:
          horizontal = 0;
          return new(false, false);

        case Op.Skip: {
          var first = this._ReadWord(word, commit);
          this._WriteVertical(v, first, commit);
          var second = this._ReadWord(word + 1, commit);
          horizontal = unchecked(second - this._ReadVertical(v + 1, commit));
          this._WriteVertical(v + 1, second, commit);
          return new(false, false);
        }

        case Op.Output: {
          var value = unchecked(this._ReadVertical(v, commit) + horizontal);
          this._WriteWord(word, value, commit);
          this._WriteVertical(v, value, commit);
          error = this._owner._WordError(value, this._target, word / this._wordsPerRow, v);
          return new(false, false);
        }

        default: {
          var table = op == Op.Chroma ? this._chroma : this._luma;
          var pair = table[index];
          horizontal = unchecked(horizontal + (pair >> 1));
          if ((pair & 1) != 0)
            return new(true, true);

          ++index;
          return new(false, false);
        }
      }
    }

    /// <summary>Tries every byte the decoder could read here and keeps the closest.</summary>
    private Choice _Choose(bool escapeAllowed) {
      if (this._at >= this._ops.Length)
        // The decoder reads one byte past the last step; any byte it can read without a second does.
        return new(1, -1);

      var best = new Choice(escapeAllowed ? 1 : 0, -1);
      var bestScore = double.MaxValue;
      for (var b = escapeAllowed ? 1 : 0; b < 256; ++b) {
        var score = this._Simulate(new(b, -1));
        if (score < bestScore) {
          bestScore = score;
          best = new(b, -1);
        }
      }

      if (escapeAllowed && bestScore > 0) {
        for (var k = 0; k < 256; ++k) {
          var score = this._Simulate(new(0, k));
          if (score < bestScore) {
            bestScore = score;
            best = new(0, k);
          }
        }
      }

      return best;
    }

    /// <summary>Mean error per picture word of the steps one choice leads to before the next byte is read.</summary>
    private double _Simulate(Choice choice) {
      this._overlayCount = 0;
      this._overlayVerticalCount = 0;
      var horizontal = this._horizontal;
      var index = this._index;
      var at = this._at;

      if (choice.IsEscape) {
        if (this._ApplyEscape(choice.Escape, ref horizontal, ref index, this._EscapeTableForLastStep()).Pending)
          return this._Pending(horizontal, at);
      } else {
        index = choice.Byte * 4;
      }

      long total = 0;
      var outputs = 0;
      while (at < this._ops.Length) {
        var fetch = this._Step(commit: false, ref horizontal, ref index, ref at, out var error);
        if (this._ops[at - 1].Op == Op.Output) {
          total += error;
          ++outputs;
        }

        if (fetch.Pending || this._overlayCount >= _OVERLAY - 2)
          break;
      }

      var pending = this._PendingError(horizontal, at);
      return outputs + (pending.Counted ? 1 : 0) == 0
        ? 0
        : (total + pending.Error) / (double)(outputs + (pending.Counted ? 1 : 0));
    }

    private double _Pending(uint horizontal, int at) {
      var (error, counted) = this._PendingError(horizontal, at);
      return counted ? error : 0;
    }

    /// <summary>
    /// What the next word would cost if it were output with the predictor as it stands — the estimate
    /// for steps a choice has started and the next byte will finish.
    /// </summary>
    private (long Error, bool Counted) _PendingError(uint horizontal, int at) {
      // Steps still to come before the next word would add a luminance delta the next byte chooses;
      // the estimate credits the best one the table offers, so a choice is not blamed for a step it
      // has not taken yet.
      var lumaToCome = false;
      for (var i = at; i < this._ops.Length; ++i) {
        var (op, word) = this._ops[i];
        switch (op) {
          case Op.Luma:
            lumaToCome = true;
            continue;
          case Op.Chroma:
            continue;
          case Op.Output: {
            var v = word % this._wordsPerRow;
            var row = word / this._wordsPerRow;
            var basis = unchecked(this._ReadVertical(v, false) + horizontal);
            var best = this._owner._WordError(basis, this._target, row, v);
            if (lumaToCome)
              foreach (var delta in this._DistinctLuma())
                best = Math.Min(best, this._owner._WordError(unchecked(basis + delta), this._target, row, v));
            return (best, true);
          }
          default:
            return (0, false);
        }
      }
      return (0, false);
    }

    private uint[]? _distinctLuma;

    private uint[] _DistinctLuma() {
      if (this._distinctLuma != null)
        return this._distinctLuma;

      var set = new HashSet<uint>();
      for (var i = 0; i < TableSize; ++i)
        if (this._luma[i] != 0 || i == 0)
          set.Add(this._luma[i] >> 1);
      return this._distinctLuma = [.. set];
    }

    private uint _ReadWord(int word, bool commit) {
      if (!commit)
        for (var i = this._overlayCount - 1; i >= 0; --i)
          if (this._overlayWord[i] == word)
            return this._overlayValue[i];
      return this._owner._reconstruction[word];
    }

    private void _WriteWord(int word, uint value, bool commit) {
      if (commit) {
        this._owner._reconstruction[word] = value;
        return;
      }
      this._overlayWord[this._overlayCount] = word;
      this._overlayValue[this._overlayCount++] = value;
    }

    private uint _ReadVertical(int v, bool commit) {
      if (!commit)
        for (var i = this._overlayVerticalCount - 1; i >= 0; --i)
          if (this._overlayVertical[i] == v)
            return this._overlayVerticalValue[i];
      return this._owner._vertical[v];
    }

    private void _WriteVertical(int v, uint value, bool commit) {
      if (commit) {
        this._owner._vertical[v] = value;
        return;
      }
      if (this._overlayVerticalCount == _OVERLAY) {
        // Shift out the oldest; a simulation never reads that far back.
        Array.Copy(this._overlayVertical, 1, this._overlayVertical, 0, _OVERLAY - 1);
        Array.Copy(this._overlayVerticalValue, 1, this._overlayVerticalValue, 0, _OVERLAY - 1);
        --this._overlayVerticalCount;
      }
      this._overlayVertical[this._overlayVerticalCount] = v;
      this._overlayVerticalValue[this._overlayVerticalCount++] = value;
    }
  }
}
