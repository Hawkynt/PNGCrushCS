using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Codecs.Svq3;

/// <summary>One picture of SVQ3: three padded planes, its 4x4-block vectors and its macroblock types.</summary>
internal sealed class Svq3Frame {

  /// <summary>Samples around each plane, so that predictors at the edges read memory that exists.</summary>
  internal const int Pad = 32;

  internal Svq3Frame(int macroblockWidth, int macroblockHeight) {
    this.LumaStride = macroblockWidth * 16 + 2 * Pad;
    this.ChromaStride = macroblockWidth * 8 + 2 * Pad;
    this.Luma = new byte[this.LumaStride * (macroblockHeight * 16 + 2 * Pad)];
    this.Cb = new byte[this.ChromaStride * (macroblockHeight * 8 + 2 * Pad)];
    this.Cr = new byte[this.ChromaStride * (macroblockHeight * 8 + 2 * Pad)];
    var blocks = macroblockWidth * 4 * macroblockHeight * 4;
    this.Motion = [new short[2 * (blocks + 8)], new short[2 * (blocks + 8)]];
    this.MacroblockType = new int[(macroblockWidth + 1) * (macroblockHeight + 1)];
  }

  internal int LumaStride { get; }

  internal int ChromaStride { get; }

  internal byte[] Luma { get; }

  internal byte[] Cb { get; }

  internal byte[] Cr { get; }

  /// <summary>The vectors of each 4x4 block, x then y, offset by four blocks as FFmpeg's are.</summary>
  internal short[][] Motion { get; }

  internal int[] MacroblockType { get; }

  internal bool Valid { get; set; }

  internal int LumaOrigin => Pad * this.LumaStride + Pad;

  internal int ChromaOrigin => Pad * this.ChromaStride + Pad;
}

/// <summary>
/// Decodes Sorenson Video 3: an early draft of H.264 — the same intra predictors, 4x4 transforms
/// and motion vector prediction — under Sorenson's own entropy coding, transform scaling, third-sample
/// motion and frame header.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/svq3.c</c> (LGPL-2.1-or-later) with the parts of
/// <c>h264pred.c</c>, <c>h264data.c</c>, <c>h264_parse.c</c>, <c>h264idct_template.c</c>,
/// <c>tpeldsp.c</c> and <c>hpeldsp.c</c> it calls. Coefficients and modes are coded with interleaved
/// exp-Golomb codes; intra pictures use H.264's 4x4 and 16x16 predictors with SVQ3's own diagonal and
/// plane ones; predicted pictures carry one to sixteen vectors a macroblock at whole, half or
/// third-sample precision; B pictures predict from the next anchor with vectors scaled by the
/// picture distance and average two directions. FFmpeg's checks of the 4x4 prediction modes report a
/// mode that cannot be used and change nothing after it, and that is kept.
/// </remarks>
internal sealed class Svq3Decoder {

  private const int _FULLPEL = 1;
  private const int _HALFPEL = 2;
  private const int _THIRDPEL = 3;
  private const int _PREDICT = 4;

  private const int _NOT_AVAILABLE = -2;

  private const int _TYPE_INTRA4x4 = 1;
  private const int _TYPE_INTRA16x16 = 2;
  private const int _TYPE_16x16 = 4;
  private const int _TYPE_SKIP = 8;

  private const int _I = 1;
  private const int _P = 2;
  private const int _B = 3;

  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _macroblockStride;
  private readonly int _blockStride;
  private readonly int _horizontalEdge;
  private readonly int _verticalEdge;
  private readonly bool _halfPel;
  private readonly bool _thirdPel;
  private readonly bool _lowDelay;
  private readonly bool _hasWatermark;
  private readonly uint _watermarkKey;

  private Svq3Frame _current;
  private Svq3Frame _last;
  private Svq3Frame _next;

  private readonly sbyte[] _intra4x4Mode;
  private readonly int[] _macroblockToRing;
  private readonly sbyte[] _modeCache = new sbyte[5 * 8];
  private readonly short[,,] _vectorCache = new short[2, 5 * 8, 2];
  private readonly sbyte[,] _referenceCache = new sbyte[2, 5 * 8];
  private readonly short[] _coefficients = new short[16 * 48];
  private readonly short[] _lumaDc = new short[32];
  private readonly byte[] _nonZero = new byte[15 * 8];
  private readonly int[,] _dequant4 = new int[52, 16];

  private Svq3BitReader _gb;
  private Svq3BitReader _slice;
  private int _qscale;
  private bool _adaptiveQuant;
  private int _sliceNumber;
  private int _sliceType;
  private int _pictureType;
  private int _frameNumber;
  private int _frameNumberOffset;
  private int _previousFrameNumber;
  private int _previousFrameNumberOffset;
  private int _cbp;
  private int _chromaMode;
  private int _intra16x16Mode;
  private int _topAvailable;
  private int _leftAvailable;
  private int _x;
  private int _y;
  private int _xy;

  internal static ReadOnlySpan<byte> Scan8 => [
    4 + 1 * 8, 5 + 1 * 8, 4 + 2 * 8, 5 + 2 * 8, 6 + 1 * 8, 7 + 1 * 8, 6 + 2 * 8, 7 + 2 * 8,
    4 + 3 * 8, 5 + 3 * 8, 4 + 4 * 8, 5 + 4 * 8, 6 + 3 * 8, 7 + 3 * 8, 6 + 4 * 8, 7 + 4 * 8,
    4 + 6 * 8, 5 + 6 * 8, 4 + 7 * 8, 5 + 7 * 8, 6 + 6 * 8, 7 + 6 * 8, 6 + 7 * 8, 7 + 7 * 8,
    4 + 8 * 8, 5 + 8 * 8, 4 + 9 * 8, 5 + 9 * 8, 6 + 8 * 8, 7 + 8 * 8, 6 + 9 * 8, 7 + 9 * 8,
    4 + 11 * 8, 5 + 11 * 8, 4 + 12 * 8, 5 + 12 * 8, 6 + 11 * 8, 7 + 11 * 8, 6 + 12 * 8, 7 + 12 * 8,
    4 + 13 * 8, 5 + 13 * 8, 4 + 14 * 8, 5 + 14 * 8, 6 + 13 * 8, 7 + 13 * 8, 6 + 14 * 8, 7 + 14 * 8,
  ];

  internal Svq3Decoder(int width, int height, bool halfPel, bool thirdPel, bool lowDelay, bool hasWatermark, uint watermarkKey) {
    this.Width = width;
    this.Height = height;
    this._halfPel = halfPel;
    this._thirdPel = thirdPel;
    this._lowDelay = lowDelay;
    this._hasWatermark = hasWatermark;
    this._watermarkKey = watermarkKey;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._macroblockStride = this._macroblockWidth + 1;
    this._blockStride = 4 * this._macroblockWidth;
    this._horizontalEdge = this._macroblockWidth * 16;
    this._verticalEdge = this._macroblockHeight * 16;
    this._current = new(this._macroblockWidth, this._macroblockHeight);
    this._last = new(this._macroblockWidth, this._macroblockHeight);
    this._next = new(this._macroblockWidth, this._macroblockHeight);
    this._intra4x4Mode = new sbyte[this._macroblockStride * 2 * 8];
    this._macroblockToRing = new int[this._macroblockStride * (this._macroblockHeight + 1)];
    for (var y = 0; y < this._macroblockHeight; ++y)
      for (var x = 0; x < this._macroblockWidth; ++x) {
        var xy = x + y * this._macroblockStride;
        this._macroblockToRing[xy] = 8 * (xy % (2 * this._macroblockStride));
      }

    for (var q = 0; q < 52; ++q) {
      var shift = Svq3Tables.QuantDiv6[q] + 2;
      var index = Svq3Tables.QuantRem6[q];
      for (var x = 0; x < 16; ++x)
        this._dequant4[q, (x >> 2) | ((x << 2) & 0xF)] = (Svq3Tables.Dequant4CoeffInit[index * 3 + (x & 1) + ((x >> 2) & 1)] * 16) << shift;
    }
  }

  internal int Width { get; }

  internal int Height { get; }

  /// <summary>
  /// Decodes a packet; returns the picture to show, which with B pictures is the anchor before the
  /// last, or <c>null</c> where there is none yet.
  /// </summary>
  internal Svq3Frame? Decode(byte[] data) {
    this._x = this._y = this._xy = 0;
    this._gb = new(data, data.Length * 8);
    this._SliceHeader();
    if (data.Length < this._macroblockWidth * this._macroblockHeight / 8)
      throw new InvalidDataException("This SVQ3 packet is too short for its picture.");

    this._pictureType = this._sliceType;
    if (this._pictureType != _B)
      (this._next, this._last) = (this._last, this._next);

    var current = this._current;
    current.Valid = false;
    Array.Clear(current.Luma);
    Array.Clear(current.Cb);
    Array.Clear(current.Cr);

    if (this._pictureType != _I) {
      if (!this._last.Valid)
        _DummyFrame(this._last);
      if (this._pictureType == _B && !this._next.Valid)
        _DummyFrame(this._next);
    }

    if (this._pictureType == _B) {
      this._frameNumberOffset = this._sliceNumber - this._previousFrameNumber;
      if (this._frameNumberOffset < 0)
        this._frameNumberOffset += 256;
      if (this._frameNumberOffset == 0 || this._frameNumberOffset >= this._previousFrameNumberOffset)
        throw new InvalidDataException("An SVQ3 B picture's picture number lies outside the distance between its anchors.");
    } else {
      this._previousFrameNumber = this._frameNumber;
      this._frameNumber = this._sliceNumber;
      this._previousFrameNumberOffset = this._frameNumber - this._previousFrameNumber;
      if (this._previousFrameNumberOffset < 0)
        this._previousFrameNumberOffset += 256;
    }

    for (var m = 0; m < 2; ++m)
      for (var i = 0; i < 4; ++i) {
        int j;
        for (j = -1; j < 4; ++j)
          this._referenceCache[m, Scan8[0] + 8 * i + j] = 1;
        if (i < 3)
          this._referenceCache[m, Scan8[0] + 8 * i + j] = _NOT_AVAILABLE;
      }

    for (this._y = 0; this._y < this._macroblockHeight; ++this._y)
      for (this._x = 0; this._x < this._macroblockWidth; ++this._x) {
        this._xy = this._x + this._y * this._macroblockStride;
        if (this._slice.BitsLeft <= 7 && ((this._slice.Position & 7) == 0 || this._slice.Peek(this._slice.BitsLeft & 7) == 0))
          this._SliceHeader();

        var type = this._slice.ReadUe();
        if (this._pictureType == _I)
          type += 8;
        else if (this._pictureType == _B && type >= 4)
          type += 4;
        if (type > 33)
          throw new InvalidDataException($"An SVQ3 macroblock states type {type}.");
        this._DecodeMacroblock(type);
        if (type != 0 || this._cbp != 0)
          this._Reconstruct();

        if (this._pictureType != _B && !this._lowDelay)
          current.MacroblockType[this._xy] = this._pictureType == _P && type < 8 ? (int)type - 1 : -1;
      }

    if (data.Length * 8 - this._slice.Position < 0)
      throw new InvalidDataException("This SVQ3 picture reads past the end of its packet.");

    current.Valid = true;
    Svq3Frame? output = this._pictureType == _B || this._lowDelay ? current : this._last.Valid ? this._last : null;
    var show = this._last.Valid || this._lowDelay;
    if (this._pictureType != _B) {
      (this._current, this._next) = (this._next, this._current);
    } else {
      // The B picture is handed out and then its buffer is reused.
      output = _Copy(current);
    }

    return show ? output : null;
  }

  /// <summary>The anchor still held back at the end of a stream with B pictures.</summary>
  internal Svq3Frame? Flush() => this._next.Valid && !this._lowDelay ? this._next : null;

  private Svq3Frame _Copy(Svq3Frame frame) {
    var copy = new Svq3Frame(this._macroblockWidth, this._macroblockHeight);
    frame.Luma.CopyTo(copy.Luma, 0);
    frame.Cb.CopyTo(copy.Cb, 0);
    frame.Cr.CopyTo(copy.Cr, 0);
    copy.Valid = true;
    return copy;
  }

  private void _DummyFrame(Svq3Frame frame) {
    Array.Clear(frame.Luma);
    Array.Fill(frame.Cb, (byte)0x80);
    Array.Fill(frame.Cr, (byte)0x80);
    frame.Valid = true;
  }

  // ============================================================================================
  // Slice header
  // ============================================================================================

  private void _SliceHeader() {
    var header = this._gb.Read(8);
    if (((header & 0x9F) != 1 && (header & 0x9F) != 2) || (header & 0x60) == 0)
      throw new InvalidDataException($"This SVQ3 packet's slice header is {header:X2}, which is not one FFmpeg knows.");

    var length = (header >> 5) & 3;
    var sliceLength = this._gb.Peek(8 * length);
    var sliceBits = sliceLength * 8;
    var sliceBytes = sliceLength + length - 1;
    this._gb.Skip(8);
    if (sliceBytes * 8L > this._gb.BitsLeft)
      throw new InvalidDataException("An SVQ3 slice runs past the end of its packet.");

    var buffer = new byte[sliceBytes + 8];
    var start = this._gb.Position / 8;
    Array.Copy(this._gb.Data, start, buffer, 0, Math.Min(sliceBytes, this._gb.Data.Length - start));
    if (length > 0)
      Array.Copy(buffer, sliceLength, buffer, 0, length - 1);
    if (this._watermarkKey != 0)
      System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
        buffer.AsSpan(1), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(1)) ^ this._watermarkKey);

    this._slice = new(buffer, sliceBits);
    this._gb.Skip(sliceBytes * 8);

    var id = this._slice.ReadUe();
    if (id >= 3)
      throw new InvalidDataException($"An SVQ3 slice states slice type {id}.");
    this._sliceType = id switch { 0 => _P, 1 => _B, _ => _I };

    if ((header & 0x9F) == 2) {
      var mbs = this._macroblockWidth * this._macroblockHeight;
      this._slice.Read(mbs < 64 ? 6 : 1 + (31 - int.LeadingZeroCount(mbs - 1)));
    } else if (this._slice.Read(1) != 0) {
      throw new NotSupportedException("This SVQ3 slice is encrypted with a media key, which FFmpeg does not decode either.");
    }

    this._sliceNumber = this._slice.Read(8);
    this._qscale = this._slice.Read(5);
    this._adaptiveQuant = this._slice.Read(1) != 0;
    this._slice.Skip(1);
    if (this._hasWatermark)
      this._slice.Skip(1);
    this._slice.Skip(1);
    this._slice.Skip(2);
    if (!this._slice.SkipStopAndDataBits())
      throw new InvalidDataException("An SVQ3 slice header ends inside its extension bytes.");

    var ring = this._intra4x4Mode;
    if (this._x > 0) {
      ring.AsSpan(this._macroblockToRing[this._xy - 1] + 3, 4).Fill(-1);
      ring.AsSpan(this._macroblockToRing[this._xy - this._x], 8 * this._x).Fill(-1);
    }

    if (this._y > 0) {
      ring.AsSpan(this._macroblockToRing[this._xy - this._macroblockStride], 8 * (this._macroblockWidth - this._x)).Fill(-1);
      if (this._x > 0)
        ring[this._macroblockToRing[this._xy - this._macroblockStride - 1] + 3] = -1;
    }
  }

  // ============================================================================================
  // Macroblock layer: svq3_decode_mb
  // ============================================================================================

  private int _Ring(int xy) => this._macroblockToRing[xy];

  private void _DecodeMacroblock(uint rawType) {
    var type = (int)rawType;
    var cbp = 0;
    var blockXy = 4 * this._x + 4 * this._y * this._blockStride;
    var current = this._current;
    int macroblockType;

    this._topAvailable = this._y == 0 ? 0x33FF : 0xFFFF;
    this._leftAvailable = this._x == 0 ? 0x5F5F : 0xFFFF;

    if (type == 0) {
      if (this._pictureType == _P || this._next.MacroblockType[this._xy] == -1) {
        this._McPart(16 * this._x, 16 * this._y, 16, 16, 0, 0, 0, false, 0, false);
        if (this._pictureType == _B)
          this._McPart(16 * this._x, 16 * this._y, 16, 16, 0, 0, 0, false, 1, true);
        macroblockType = _TYPE_SKIP;
      } else {
        var size = Math.Min(this._next.MacroblockType[this._xy], 6);
        this._McDirection(size, _PREDICT, 0, false);
        this._McDirection(size, _PREDICT, 1, true);
        macroblockType = _TYPE_16x16;
      }
    } else if (type < 8) {
      int mode;
      if (this._thirdPel && this._halfPel == (this._slice.Read(1) == 0))
        mode = _THIRDPEL;
      else if (this._halfPel && this._thirdPel == (this._slice.Read(1) == 0))
        mode = _HALFPEL;
      else
        mode = _FULLPEL;

      for (var m = 0; m < 2; ++m) {
        var motion = current.Motion[m];
        if (this._x > 0 && this._intra4x4Mode[this._Ring(this._xy - 1) + 6] != -1) {
          for (var i = 0; i < 4; ++i)
            this._LoadVector(m, Scan8[0] - 1 + i * 8, motion, blockXy - 1 + i * this._blockStride);
        } else {
          for (var i = 0; i < 4; ++i)
            this._SetVector(m, Scan8[0] - 1 + i * 8, 0, 0);
        }

        if (this._y > 0) {
          for (var i = 0; i < 4; ++i)
            this._LoadVector(m, Scan8[0] - 8 + i, motion, blockXy - this._blockStride + i);
          var top = this._intra4x4Mode[this._Ring(this._xy - this._macroblockStride)] == -1 ? _NOT_AVAILABLE : 1;
          for (var i = 0; i < 4; ++i)
            this._referenceCache[m, Scan8[0] - 8 + i] = (sbyte)top;

          if (this._x < this._macroblockWidth - 1) {
            this._LoadVector(m, Scan8[0] + 4 - 8, motion, blockXy - this._blockStride + 4);
            this._referenceCache[m, Scan8[0] + 4 - 8] =
              (sbyte)(this._intra4x4Mode[this._Ring(this._xy - this._macroblockStride + 1) + 6] == -1
                      || this._intra4x4Mode[this._Ring(this._xy - this._macroblockStride)] == -1 ? _NOT_AVAILABLE : 1);
          } else {
            this._referenceCache[m, Scan8[0] + 4 - 8] = _NOT_AVAILABLE;
          }

          if (this._x > 0) {
            this._LoadVector(m, Scan8[0] - 1 - 8, motion, blockXy - this._blockStride - 1);
            this._referenceCache[m, Scan8[0] - 1 - 8] =
              (sbyte)(this._intra4x4Mode[this._Ring(this._xy - this._macroblockStride - 1) + 3] == -1 ? _NOT_AVAILABLE : 1);
          } else {
            this._referenceCache[m, Scan8[0] - 1 - 8] = _NOT_AVAILABLE;
          }
        } else {
          for (var i = 0; i < 8; ++i)
            this._referenceCache[m, Scan8[0] - 8 - 1 + i] = _NOT_AVAILABLE;
        }

        if (this._pictureType != _B)
          break;
      }

      if (this._pictureType == _P) {
        this._McDirection(type - 1, mode, 0, false);
      } else {
        if (type != 2)
          this._McDirection(0, mode, 0, false);
        else
          this._ClearVectors(0, blockXy);
        if (type != 1)
          this._McDirection(0, mode, 1, type == 3);
        else
          this._ClearVectors(1, blockXy);
      }

      macroblockType = _TYPE_16x16;
    } else if (type == 8 || type == 33) {
      var ring = this._Ring(this._xy);
      var cache = this._modeCache;
      Array.Fill(cache, (sbyte)-1);

      if (type == 8) {
        if (this._x > 0) {
          for (var i = 0; i < 4; ++i)
            cache[Scan8[0] - 1 + i * 8] = this._intra4x4Mode[this._Ring(this._xy - 1) + 6 - i];
          if (cache[Scan8[0] - 1] == -1)
            this._leftAvailable = 0x5F5F;
        }

        if (this._y > 0) {
          var above = this._Ring(this._xy - this._macroblockStride);
          for (var i = 0; i < 4; ++i)
            cache[4 + i] = this._intra4x4Mode[above + i];
          if (cache[4] == -1)
            this._topAvailable = 0x33FF;
        }

        for (var i = 0; i < 16; i += 2) {
          var vlc = this._slice.ReadUe();
          if (vlc >= 25)
            throw new InvalidDataException($"An SVQ3 4x4 prediction code is {vlc}.");
          var left = Scan8[i] - 1;
          var topAt = Scan8[i] - 8;
          cache[left + 1] = Svq3Tables.Prediction1[(cache[topAt] + 1) * 30 + (cache[left] + 1) * 5 + Svq3Tables.Prediction0[vlc * 2]];
          cache[left + 2] = Svq3Tables.Prediction1[(cache[topAt + 1] + 1) * 30 + (cache[left + 1] + 1) * 5 + Svq3Tables.Prediction0[vlc * 2 + 1]];
          if (cache[left + 1] == -1 || cache[left + 2] == -1)
            throw new InvalidDataException("An SVQ3 4x4 prediction code names no mode.");
        }
      } else {
        for (var i = 0; i < 4; ++i)
          cache.AsSpan(Scan8[0] + 8 * i, 4).Fill(Svq3IntraPrediction.DcPrediction);
      }

      for (var i = 0; i < 4; ++i)
        this._intra4x4Mode[ring + i] = cache[4 + 8 * 4 + i];
      this._intra4x4Mode[ring + 4] = cache[7 + 8 * 3];
      this._intra4x4Mode[ring + 5] = cache[7 + 8 * 2];
      this._intra4x4Mode[ring + 6] = cache[7 + 8 * 1];

      if (type == 8) {
        this._CheckIntra4x4Modes();
        this._topAvailable = this._y == 0 ? 0x33FF : 0xFFFF;
        this._leftAvailable = this._x == 0 ? 0x5F5F : 0xFFFF;
      } else {
        for (var i = 0; i < 4; ++i)
          cache.AsSpan(Scan8[0] + 8 * i, 4).Fill(Svq3IntraPrediction.Dc128Prediction);
        this._topAvailable = 0x33FF;
        this._leftAvailable = 0x5F5F;
      }

      macroblockType = _TYPE_INTRA4x4;
    } else {
      int direction = Svq3Tables.IntraMacroblockPredictionMode[type - 8];
      direction = (direction >> 1) ^ (3 * (direction & 1)) ^ 1;
      this._intra16x16Mode = this._CheckIntraMode(direction, chroma: false);
      cbp = Svq3Tables.IntraMacroblockCbp[type - 8];
      macroblockType = _TYPE_INTRA16x16;
    }

    var isIntra = (macroblockType & (_TYPE_INTRA4x4 | _TYPE_INTRA16x16)) != 0;
    // Everything that is not a 16x16 inter partition — intra, and skipped too — leaves no vector.
    if (macroblockType != _TYPE_16x16 && this._pictureType != _I) {
      this._ClearVectors(0, blockXy);
      if (this._pictureType == _B)
        this._ClearVectors(1, blockXy);
    }

    if (macroblockType != _TYPE_INTRA4x4)
      this._intra4x4Mode.AsSpan(this._Ring(this._xy), 8).Fill(Svq3IntraPrediction.DcPrediction);
    if (macroblockType != _TYPE_SKIP || this._pictureType == _B)
      Array.Clear(this._nonZero, 8, 14 * 8);

    if (macroblockType != _TYPE_INTRA16x16 && (macroblockType != _TYPE_SKIP || this._pictureType == _B)) {
      var vlc = this._slice.ReadUe();
      if (vlc >= 48)
        throw new InvalidDataException($"An SVQ3 coded block pattern code is {vlc}.");
      cbp = isIntra ? Svq3Tables.GolombToIntra4x4Cbp[vlc] : Svq3Tables.GolombToInterCbp[vlc];
    }

    if (macroblockType == _TYPE_INTRA16x16 || (this._pictureType != _I && this._adaptiveQuant && cbp != 0)) {
      this._qscale += this._slice.ReadSe();
      if ((uint)this._qscale > 31)
        throw new InvalidDataException($"An SVQ3 macroblock's quantiser becomes {this._qscale}.");
    }

    if (macroblockType == _TYPE_INTRA16x16) {
      Array.Clear(this._lumaDc);
      this._DecodeBlock(this._lumaDc, 0, 0, 1);
    }

    if (cbp != 0) {
      var index = macroblockType == _TYPE_INTRA16x16 ? 1 : 0;
      var blockType = this._qscale < 24 && macroblockType == _TYPE_INTRA4x4 ? 2 : 1;
      for (var i = 0; i < 4; ++i)
        if ((cbp & (1 << i)) != 0)
          for (var j = 0; j < 4; ++j) {
            var k = index != 0 ? (j & 1) + 2 * (i & 1) + 2 * (j & 2) + 4 * (i & 2) : 4 * i + j;
            this._nonZero[Scan8[k]] = 1;
            this._DecodeBlock(this._coefficients, 16 * k, index, blockType);
          }

      if ((cbp & 0x30) != 0) {
        for (var i = 1; i < 3; ++i)
          this._DecodeBlock(this._coefficients, 16 * 16 * i, 0, 3);
        if ((cbp & 0x20) != 0)
          for (var i = 1; i < 3; ++i)
            for (var j = 0; j < 4; ++j) {
              var k = 16 * i + j;
              this._nonZero[Scan8[k]] = 1;
              this._DecodeBlock(this._coefficients, 16 * k, 1, 1);
            }
      }
    }

    this._cbp = cbp;
    current.MacroblockType[this._xy] = macroblockType;
    this._currentType = macroblockType;

    if (isIntra)
      this._chromaMode = this._CheckIntraMode(Svq3IntraPrediction.Dc8x8, chroma: true);
  }

  private int _currentType;

  private void _LoadVector(int list, int cacheAt, short[] motion, int block) {
    this._vectorCache[list, cacheAt, 0] = motion[2 * (block + 4)];
    this._vectorCache[list, cacheAt, 1] = motion[2 * (block + 4) + 1];
  }

  private void _SetVector(int list, int cacheAt, int x, int y) {
    this._vectorCache[list, cacheAt, 0] = (short)x;
    this._vectorCache[list, cacheAt, 1] = (short)y;
  }

  private void _ClearVectors(int list, int blockXy) {
    var motion = this._current.Motion[list];
    for (var i = 0; i < 4; ++i)
      Array.Clear(motion, 2 * (blockXy + i * this._blockStride + 4), 8);
  }

  /// <summary><c>ff_h264_check_intra4x4_pred_mode</c>, whose result SVQ3 ignores: it stops at the first mode it cannot fix.</summary>
  private void _CheckIntra4x4Modes() {
    ReadOnlySpan<sbyte> top = [-1, 0, Svq3IntraPrediction.LeftDcPrediction, -1, -1, -1, -1, -1, 0, 0, 0, 0];
    ReadOnlySpan<sbyte> left = [0, -1, Svq3IntraPrediction.TopDcPrediction, 0, -1, -1, -1, 0, -1, Svq3IntraPrediction.Dc128Prediction, 0, 0];
    var cache = this._modeCache;
    if ((this._topAvailable & 0x8000) == 0)
      for (var i = 0; i < 4; ++i) {
        var status = top[cache[Scan8[0] + i]];
        if (status < 0)
          return;
        if (status != 0)
          cache[Scan8[0] + i] = status;
      }

    if ((this._leftAvailable & 0x8888) != 0x8888) {
      ReadOnlySpan<int> mask = [0x8000, 0x2000, 0x80, 0x20];
      for (var i = 0; i < 4; ++i)
        if ((this._leftAvailable & mask[i]) == 0) {
          var status = left[cache[Scan8[0] + 8 * i]];
          if (status < 0)
            return;
          if (status != 0)
            cache[Scan8[0] + 8 * i] = status;
        }
    }
  }

  /// <summary><c>ff_h264_check_intra_pred_mode</c>.</summary>
  private int _CheckIntraMode(int mode, bool chroma) {
    ReadOnlySpan<int> top = [Svq3IntraPrediction.LeftDc8x8, 1, -1, -1];
    ReadOnlySpan<int> left = [Svq3IntraPrediction.TopDc8x8, -1, 2, -1, Svq3IntraPrediction.Dc128x8];
    if ((uint)mode > 3)
      throw new InvalidDataException("An SVQ3 intra prediction mode is out of range.");
    if ((this._topAvailable & 0x8000) == 0) {
      mode = top[mode];
      if (mode < 0)
        throw new InvalidDataException("An SVQ3 intra mode needs the row above, which is not there.");
    }

    if ((this._leftAvailable & 0x8080) != 0x8080) {
      mode = left[mode];
      if (mode < 0)
        throw new InvalidDataException("An SVQ3 intra mode needs the column to the left, which is not there.");
      if (chroma && (this._leftAvailable & 0x8080) != 0)
        throw new NotSupportedException("An SVQ3 chroma prediction with half a left column is not implemented.");
    }

    return mode;
  }

  /// <summary><c>svq3_decode_block</c>.</summary>
  private void _DecodeBlock(short[] block, int offset, int index, int type) {
    var intra = 3 * type >> 2;
    ReadOnlySpan<byte> scan = type switch {
      0 => Svq3Tables.LumaDcZigzagScan,
      1 => Svq3Tables.ZigzagScan,
      2 => Svq3Tables.Svq3Scan,
      _ => Svq3Tables.ChromaDcScan,
    };

    for (var limit = 16 >> intra; index < 16; index = limit, limit += 8) {
      uint vlc;
      for (; (vlc = this._slice.ReadUe()) != 0; ++index) {
        if ((int)vlc < 0)
          throw new InvalidDataException("An SVQ3 coefficient code overflows.");
        var sign = (vlc & 1) != 0 ? 0 : -1;
        vlc = (vlc + 1) >> 1;
        int run, level;
        if (type == 3) {
          if (vlc < 3) {
            run = 0;
            level = (int)vlc;
          } else if (vlc < 4) {
            run = 1;
            level = 1;
          } else {
            run = (int)(vlc & 3);
            level = (int)((vlc + 9) >> 2) - run;
          }
        } else if (vlc < 16) {
          run = Svq3Tables.DctRun[intra * 16 + vlc];
          level = Svq3Tables.DctLevel[intra * 16 + vlc];
        } else if (intra != 0) {
          run = (int)(vlc & 7);
          level = (int)(vlc >> 3) + (run == 0 ? 8 : run < 2 ? 2 : run < 5 ? 0 : -1);
        } else {
          run = (int)(vlc & 0xF);
          level = (int)(vlc >> 4) + (run == 0 ? 4 : run < 3 ? 2 : run < 10 ? 1 : 0);
        }

        if ((index += run) >= limit)
          throw new InvalidDataException("An SVQ3 block's coefficients run past it.");
        block[offset + scan[index]] = (short)((level ^ sign) - sign);
      }

      if (type != 2)
        break;
    }
  }

  // ============================================================================================
  // Motion
  // ============================================================================================

  /// <summary><c>svq3_pred_motion</c>.</summary>
  private (int X, int Y) _PredictMotion(int n, int partWidth, int list, int reference) {
    var index = Scan8[n];
    int topReference = this._referenceCache[list, index - 8];
    int leftReference = this._referenceCache[list, index - 1];
    var aAt = index - 1;
    var bAt = index - 8;
    int cAt;
    int diagonalReference = this._referenceCache[list, index - 8 + partWidth];
    if (diagonalReference != _NOT_AVAILABLE) {
      cAt = index - 8 + partWidth;
    } else {
      cAt = index - 8 - 1;
      diagonalReference = this._referenceCache[list, index - 8 - 1];
    }

    int Ax(int c) => this._vectorCache[list, aAt, c];
    int Bx(int c) => this._vectorCache[list, bAt, c];
    int Cx(int c) => this._vectorCache[list, cAt, c];
    static int Median(int a, int b, int c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

    var matches = (diagonalReference == reference ? 1 : 0) + (topReference == reference ? 1 : 0) + (leftReference == reference ? 1 : 0);
    if (matches > 1)
      return (Median(Ax(0), Bx(0), Cx(0)), Median(Ax(1), Bx(1), Cx(1)));
    if (matches == 1) {
      if (leftReference == reference)
        return (Ax(0), Ax(1));
      if (topReference == reference)
        return (Bx(0), Bx(1));
      return (Cx(0), Cx(1));
    }

    if (topReference == _NOT_AVAILABLE && diagonalReference == _NOT_AVAILABLE && leftReference != _NOT_AVAILABLE)
      return (Ax(0), Ax(1));
    return (Median(Ax(0), Bx(0), Cx(0)), Median(Ax(1), Bx(1), Cx(1)));
  }

  /// <summary><c>svq3_mc_dir</c>.</summary>
  private void _McDirection(int size, int mode, int direction, bool average) {
    var partWidth = (size & 5) == 4 ? 4 : 16 >> (size & 1);
    var partHeight = 16 >> (int)((uint)(size + 1) / 3);
    var extraWidth = mode == _PREDICT ? -16 * 6 : 0;
    var horizontalEdge = 6 * (this._horizontalEdge - partWidth) - extraWidth;
    var verticalEdge = 6 * (this._verticalEdge - partHeight) - extraWidth;

    for (var i = 0; i < 16; i += partHeight)
      for (var j = 0; j < 16; j += partWidth) {
        var blockXy = 4 * this._x + (j >> 2) + (4 * this._y + (i >> 2)) * this._blockStride;
        var x = 16 * this._x + j;
        var y = 16 * this._y + i;
        var k = ((j >> 2) & 1) + ((i >> 1) & 2) + ((j >> 1) & 4) + (i & 8);
        int mx, my;
        if (mode != _PREDICT) {
          (mx, my) = this._PredictMotion(k, partWidth >> 2, direction, 1);
        } else {
          var next = this._next.Motion[0];
          mx = next[2 * (blockXy + 4)] * 2;
          my = next[2 * (blockXy + 4) + 1] * 2;
          if (direction == 0) {
            mx = (mx * this._frameNumberOffset / this._previousFrameNumberOffset + 1) >> 1;
            my = (my * this._frameNumberOffset / this._previousFrameNumberOffset + 1) >> 1;
          } else {
            mx = (mx * (this._frameNumberOffset - this._previousFrameNumberOffset) / this._previousFrameNumberOffset + 1) >> 1;
            my = (my * (this._frameNumberOffset - this._previousFrameNumberOffset) / this._previousFrameNumberOffset + 1) >> 1;
          }
        }

        mx = Math.Clamp(mx, extraWidth - 6 * x, horizontalEdge - 6 * x);
        my = Math.Clamp(my, extraWidth - 6 * y, verticalEdge - 6 * y);

        int dx = 0, dy = 0;
        if (mode != _PREDICT) {
          dy = this._slice.ReadSe();
          dx = this._slice.ReadSe();
          if (dx != (short)dx || dy != (short)dy)
            throw new InvalidDataException("An SVQ3 vector difference overflows.");
        }

        if (mode == _THIRDPEL) {
          mx = ((mx + 1) >> 1) + dx;
          my = ((my + 1) >> 1) + dy;
          var fx = (int)((uint)(mx + 0x30000) / 3) - 0x10000;
          var fy = (int)((uint)(my + 0x30000) / 3) - 0x10000;
          var dxy = (mx - 3 * fx) + 4 * (my - 3 * fy);
          this._McPart(x, y, partWidth, partHeight, fx, fy, dxy, true, direction, average);
          mx += mx;
          my += my;
        } else if (mode is _HALFPEL or _PREDICT) {
          mx = (int)((uint)(mx + 1 + 0x30000) / 3) + dx - 0x10000;
          my = (int)((uint)(my + 1 + 0x30000) / 3) + dy - 0x10000;
          var dxy = (mx & 1) + 2 * (my & 1);
          this._McPart(x, y, partWidth, partHeight, mx >> 1, my >> 1, dxy, false, direction, average);
          mx *= 3;
          my *= 3;
        } else {
          mx = (int)((uint)(mx + 3 + 0x60000) / 6) + dx - 0x10000;
          my = (int)((uint)(my + 3 + 0x60000) / 6) + dy - 0x10000;
          this._McPart(x, y, partWidth, partHeight, mx, my, 0, false, direction, average);
          mx *= 6;
          my *= 6;
        }

        if (mode != _PREDICT) {
          if (partHeight == 8 && i < 8) {
            this._SetVector(direction, Scan8[k] + 8, mx, my);
            if (partWidth == 8 && j < 8)
              this._SetVector(direction, Scan8[k] + 1 + 8, mx, my);
          }

          if (partWidth == 8 && j < 8)
            this._SetVector(direction, Scan8[k] + 1, mx, my);
          if (partWidth == 4 || partHeight == 4)
            this._SetVector(direction, Scan8[k], mx, my);
        }

        var motion = this._current.Motion[direction];
        for (var r = 0; r < partHeight >> 2; ++r)
          for (var c = 0; c < partWidth >> 2; ++c) {
            var at = 2 * (blockXy + r * this._blockStride + c + 4);
            motion[at] = (short)mx;
            motion[at + 1] = (short)my;
          }
      }
  }

  /// <summary><c>svq3_mc_dir_part</c>: one partition's luminance and chrominance from one reference.</summary>
  private void _McPart(int x, int y, int width, int height, int mx, int my, int dxy, bool thirdPel, int direction, bool average) {
    var reference = direction == 0 ? this._last : this._next;
    var current = this._current;
    mx += x;
    my += y;
    if (mx < 0 || mx >= this._horizontalEdge - width - 1 || my < 0 || my >= this._verticalEdge - height - 1) {
      mx = Math.Clamp(mx, -16, this._horizontalEdge - width + 15);
      my = Math.Clamp(my, -16, this._verticalEdge - height + 15);
    }

    _Pixels(reference.Luma, reference.LumaOrigin, reference.LumaStride, this._horizontalEdge, this._verticalEdge, mx, my,
      current.Luma, current.LumaOrigin + y * current.LumaStride + x, current.LumaStride, width, height, dxy, thirdPel, average);

    mx = (mx + (mx < x ? 1 : 0)) >> 1;
    my = (my + (my < y ? 1 : 0)) >> 1;
    width >>= 1;
    height >>= 1;
    for (var plane = 1; plane < 3; ++plane) {
      var source = plane == 1 ? reference.Cb : reference.Cr;
      var target = plane == 1 ? current.Cb : current.Cr;
      _Pixels(source, reference.ChromaOrigin, reference.ChromaStride, this._horizontalEdge >> 1, this._verticalEdge >> 1, mx, my,
        target, current.ChromaOrigin + (y >> 1) * current.ChromaStride + (x >> 1), current.ChromaStride, width, height, dxy, thirdPel, average);
    }
  }

  /// <summary>The half- and third-sample pixel operations of <c>hpeldsp</c> and <c>tpeldsp</c>, over an edge-repeated source.</summary>
  private static void _Pixels(
    byte[] source, int origin, int stride, int edgeWidth, int edgeHeight, int sx, int sy,
    byte[] target, int at, int targetStride, int width, int height, int dxy, bool thirdPel, bool average) {
    int S(int c, int r) => source[origin + Math.Clamp(sy + r, 0, edgeHeight - 1) * stride + Math.Clamp(sx + c, 0, edgeWidth - 1)];

    // hpeldsp's two-wide diagonal average never reads its destination: avg_pixels2_xy2 is a put,
    // so a 4x4 partition's chrominance in a bidirectional block takes the second prediction alone.
    if (!thirdPel && width == 2 && dxy == 3)
      average = false;

    for (var r = 0; r < height; ++r)
      for (var c = 0; c < width; ++c) {
        int value;
        if (thirdPel) {
          value = dxy switch {
            0 => S(c, r),
            1 => ((2 * S(c, r) + S(c + 1, r) + 1) * 683) >> 11,
            2 => ((S(c, r) + 2 * S(c + 1, r) + 1) * 683) >> 11,
            4 => ((2 * S(c, r) + S(c, r + 1) + 1) * 683) >> 11,
            5 => ((4 * S(c, r) + 3 * S(c + 1, r) + 3 * S(c, r + 1) + 2 * S(c + 1, r + 1) + 6) * 2731) >> 15,
            6 => ((3 * S(c, r) + 4 * S(c + 1, r) + 2 * S(c, r + 1) + 3 * S(c + 1, r + 1) + 6) * 2731) >> 15,
            8 => ((S(c, r) + 2 * S(c, r + 1) + 1) * 683) >> 11,
            9 => ((3 * S(c, r) + 2 * S(c + 1, r) + 4 * S(c, r + 1) + 3 * S(c + 1, r + 1) + 6) * 2731) >> 15,
            10 => ((2 * S(c, r) + 3 * S(c + 1, r) + 3 * S(c, r + 1) + 4 * S(c + 1, r + 1) + 6) * 2731) >> 15,
            _ => throw new InvalidDataException($"An SVQ3 third-sample vector has fraction {dxy}."),
          };
        } else {
          value = dxy switch {
            0 => S(c, r),
            1 => (S(c, r) + S(c + 1, r) + 1) >> 1,
            2 => (S(c, r) + S(c, r + 1) + 1) >> 1,
            _ => (S(c, r) + S(c + 1, r) + S(c, r + 1) + S(c + 1, r + 1) + 2) >> 2,
          };
        }

        var o = at + r * targetStride + c;
        target[o] = average ? (byte)((target[o] + value + 1) >> 1) : (byte)value;
      }
  }

  // ============================================================================================
  // Reconstruction: hl_decode_mb
  // ============================================================================================

  private void _Reconstruct() {
    var current = this._current;
    var type = this._currentType;
    var ls = current.LumaStride;
    var cs = current.ChromaStride;
    var y0 = current.LumaOrigin + this._y * 16 * ls + this._x * 16;
    var c0 = current.ChromaOrigin + this._y * 8 * cs + this._x * 8;
    var isIntra = (type & (_TYPE_INTRA4x4 | _TYPE_INTRA16x16)) != 0;

    int LumaOffset(int i) => 4 * ((Scan8[i] - Scan8[0]) & 7) + 4 * ls * ((Scan8[i] - Scan8[0]) >> 3);
    int ChromaOffset(int i) => 4 * ((Scan8[i] - Scan8[0]) & 7) + 4 * cs * ((Scan8[i] - Scan8[0]) >> 3);

    if (isIntra) {
      Svq3IntraPrediction.Predict8x8(this._chromaMode, current.Cb, c0, cs);
      Svq3IntraPrediction.Predict8x8(this._chromaMode, current.Cr, c0, cs);

      if (type == _TYPE_INTRA4x4) {
        for (var i = 0; i < 16; ++i) {
          var at = y0 + LumaOffset(i);
          int mode = this._modeCache[Scan8[i]];
          Svq3IntraPrediction.Predict4x4(mode, current.Luma, at, ls, at + 4 - ls);
          if (this._nonZero[Scan8[i]] != 0)
            this._AddIdct(current.Luma, at, ls, 16 * i, this._qscale, 0);
        }
      } else {
        Svq3IntraPrediction.Predict16x16(this._intra16x16Mode, current.Luma, y0, ls);
        this._LumaDcDequantIdct(this._qscale);
      }
    }

    if (type != _TYPE_INTRA4x4)
      for (var i = 0; i < 16; ++i)
        if (this._nonZero[Scan8[i]] != 0 || this._coefficients[i * 16] != 0)
          this._AddIdct(current.Luma, y0 + LumaOffset(i), ls, 16 * i, this._qscale, isIntra ? 1 : 0);

    if ((this._cbp & 0x30) != 0) {
      var qmul = this._dequant4[4, 0];
      _ChromaDcDequantIdct(this._coefficients, 16 * 16, qmul);
      _ChromaDcDequantIdct(this._coefficients, 16 * 16 * 2, qmul);
      var chromaQp = Svq3Tables.ChromaQp[this._qscale + 12] - 12;
      for (var j = 1; j < 3; ++j) {
        var plane = j == 1 ? current.Cb : current.Cr;
        for (var i = j * 16; i < j * 16 + 4; ++i)
          if (this._nonZero[Scan8[i]] != 0 || this._coefficients[i * 16] != 0)
            this._AddIdct(plane, c0 + ChromaOffset(i - j * 16), cs, 16 * i, chromaQp, 2);
      }
    }
  }

  /// <summary><c>svq3_luma_dc_dequant_idct_c</c>: the sixteen DCs into the first coefficient of each block.</summary>
  private void _LumaDcDequantIdct(int qp) {
    var qmul = Svq3Tables.DequantCoeff[qp];
    Span<int> temp = stackalloc int[16];
    ReadOnlySpan<int> xOffset = [0, 16, 64, 80];
    var input = this._lumaDc;
    for (var i = 0; i < 4; ++i) {
      var z0 = 13 * (input[4 * i] + input[4 * i + 2]);
      var z1 = 13 * (input[4 * i] - input[4 * i + 2]);
      var z2 = 7 * input[4 * i + 1] - 17 * input[4 * i + 3];
      var z3 = 17 * input[4 * i + 1] + 7 * input[4 * i + 3];
      temp[4 * i] = z0 + z3;
      temp[4 * i + 1] = z1 + z2;
      temp[4 * i + 2] = z1 - z2;
      temp[4 * i + 3] = z0 - z3;
    }

    unchecked {
      for (var i = 0; i < 4; ++i) {
        var offset = xOffset[i];
        var z0 = 13 * (temp[i] + temp[8 + i]);
        var z1 = 13 * (temp[i] - temp[8 + i]);
        var z2 = 7 * temp[4 + i] - 17 * temp[12 + i];
        var z3 = 17 * temp[4 + i] + 7 * temp[12 + i];
        this._coefficients[offset] = (short)((int)((uint)(z0 + z3) * qmul + 0x80000) >> 20);
        this._coefficients[32 + offset] = (short)((int)((uint)(z1 + z2) * qmul + 0x80000) >> 20);
        this._coefficients[128 + offset] = (short)((int)((uint)(z1 - z2) * qmul + 0x80000) >> 20);
        this._coefficients[160 + offset] = (short)((int)((uint)(z0 - z3) * qmul + 0x80000) >> 20);
      }
    }
  }

  /// <summary><c>svq3_add_idct_c</c>.</summary>
  private void _AddIdct(byte[] plane, int at, int stride, int offset, int qp, int dc) {
    var block = this._coefficients;
    var qmul = (int)Svq3Tables.DequantCoeff[qp];
    unchecked {
      if (dc != 0) {
        dc = (int)(13u * 13u * (dc == 1 ? 1538u * (uint)block[offset] : (uint)(qmul * (block[offset] >> 3) / 2)));
        block[offset] = 0;
      }

      for (var i = 0; i < 4; ++i) {
        var b = offset + 4 * i;
        var z0 = 13 * (block[b] + block[b + 2]);
        var z1 = 13 * (block[b] - block[b + 2]);
        var z2 = 7 * block[b + 1] - 17 * block[b + 3];
        var z3 = 17 * block[b + 1] + 7 * block[b + 3];
        block[b] = (short)(z0 + z3);
        block[b + 1] = (short)(z1 + z2);
        block[b + 2] = (short)(z1 - z2);
        block[b + 3] = (short)(z0 - z3);
      }

      var rr = (int)((uint)dc + 0x80000u);
      for (var i = 0; i < 4; ++i) {
        var z0 = (uint)(13 * (block[offset + i] + block[offset + i + 8]));
        var z1 = (uint)(13 * (block[offset + i] - block[offset + i + 8]));
        var z2 = (uint)(7 * block[offset + i + 4] - 17 * block[offset + i + 12]);
        var z3 = (uint)(17 * block[offset + i + 4] + 7 * block[offset + i + 12]);
        _AddClip(plane, at + i, (int)((z0 + z3) * (uint)qmul + (uint)rr) >> 20);
        _AddClip(plane, at + i + stride, (int)((z1 + z2) * (uint)qmul + (uint)rr) >> 20);
        _AddClip(plane, at + i + 2 * stride, (int)((z1 - z2) * (uint)qmul + (uint)rr) >> 20);
        _AddClip(plane, at + i + 3 * stride, (int)((z0 - z3) * (uint)qmul + (uint)rr) >> 20);
      }
    }

    Array.Clear(block, offset, 16);
  }

  private static void _AddClip(byte[] plane, int at, int value) {
    var v = plane[at] + value;
    plane[at] = v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
  }

  /// <summary><c>ff_h264_chroma_dc_dequant_idct</c>: the four chroma DCs, whose blocks are 16 coefficients apart.</summary>
  private static void _ChromaDcDequantIdct(short[] block, int offset, int qmul) {
    unchecked {
      int a = block[offset], b = block[offset + 16], c = block[offset + 32], d = block[offset + 48];
      var e = a - b;
      a += b;
      b = c - d;
      c += d;
      block[offset] = (short)((int)((uint)(a + c) * (uint)qmul) >> 7);
      block[offset + 16] = (short)((int)((uint)(e + b) * (uint)qmul) >> 7);
      block[offset + 32] = (short)((int)((uint)(a - c) * (uint)qmul) >> 7);
      block[offset + 48] = (short)((int)((uint)(e - b) * (uint)qmul) >> 7);
    }
  }
}

/// <summary>An MSB-first reader over a byte array with FFmpeg's zero padding past the end, and SVQ3's interleaved exp-Golomb codes.</summary>
internal struct Svq3BitReader(byte[] data, int sizeInBits) {

  internal byte[] Data { get; } = data;

  internal int Position { get; private set; }

  internal readonly int BitsLeft => sizeInBits - this.Position;

  private readonly int _Bit(int position) => position >= 0 && position < sizeInBits && (position >> 3) < this.Data.Length
    ? (this.Data[position >> 3] >> (7 - (position & 7))) & 1
    : 0;

  internal readonly int Peek(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this._Bit(this.Position + i);
    return value;
  }

  internal int Read(int count) {
    var value = this.Peek(count);
    this.Position += count;
    return value;
  }

  internal void Skip(int count) => this.Position += count;

  /// <summary><c>get_interleaved_ue_golomb</c>: a nought says another data bit follows, a one ends the code.</summary>
  internal uint ReadUe() {
    uint value = 1;
    while (this.Read(1) == 0) {
      value = (value << 1) | (uint)this.Read(1);
      if (value >= 0x8000000u || this.BitsLeft < 0)
        return value - 1;
    }

    return value - 1;
  }

  /// <summary><c>get_interleaved_se_golomb</c>.</summary>
  internal int ReadSe() {
    var k = this.ReadUe();
    var magnitude = (int)((k + 1) >> 1);
    return (k & 1) != 0 ? magnitude : -magnitude;
  }

  /// <summary><c>skip_1stop_8data_bits</c>.</summary>
  internal bool SkipStopAndDataBits() {
    if (this.BitsLeft <= 0)
      return false;
    while (this.Read(1) != 0) {
      this.Skip(8);
      if (this.BitsLeft <= 0)
        return false;
    }

    return true;
  }
}
