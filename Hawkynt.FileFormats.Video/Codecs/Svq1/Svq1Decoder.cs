using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Svq1;

/// <summary>A most-significant-bit-first reader; reads past the end return zeros, as FFmpeg's padded reader does.</summary>
internal struct Svq1BitReader(byte[] data) {
  private readonly byte[] _data = data;
  private long _position;

  public readonly long BitsLeft => (long)this._data.Length * 8 - this._position;

  public int ReadBit() {
    var at = this._position >> 3;
    var bit = at < this._data.Length ? (this._data[at] >> (7 - (int)(this._position & 7))) & 1 : 0;
    ++this._position;
    return bit;
  }

  public uint ReadBits(int count) {
    uint value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | (uint)this.ReadBit();
    return value;
  }

  public void Skip(int count) => this._position += count;
}

/// <summary>A prefix code given as (code, length) per symbol, the symbol being the index.</summary>
internal sealed class Svq1Vlc {
  private readonly Dictionary<(int Length, int Code), int> _symbols = new();
  private readonly int _maxLength;

  public Svq1Vlc(ReadOnlySpan<(int Code, int Length)> codes) {
    for (var symbol = 0; symbol < codes.Length; ++symbol) {
      if (codes[symbol].Length == 0)
        continue;
      this._symbols[(codes[symbol].Length, codes[symbol].Code)] = symbol;
      this._maxLength = Math.Max(this._maxLength, codes[symbol].Length);
    }
  }

  /// <summary>The next symbol, or -1 for a bit pattern that names none, as FFmpeg's <c>get_vlc2</c> answers.</summary>
  public int Read(ref Svq1BitReader bits) {
    var code = 0;
    for (var length = 1; length <= this._maxLength; ++length) {
      code = (code << 1) | bits.ReadBit();
      if (this._symbols.TryGetValue((length, code), out var symbol))
        return symbol;
    }
    return -1;
  }
}

/// <summary>Sorenson Video 1: multi-stage vector quantisation over a quadtree of 16x16 blocks, YUV 4:1:0.</summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/svq1dec.c</c> (Arpi, Michael Niedermayer, Mike Melanson and
/// others), LGPL-2.1-or-later; see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this file. This is the
/// codec <c>codec-investigations.md</c> recorded as stopped on a codebook only FFmpeg's sources print.
/// <para/>
/// <b>Four samples a word.</b> A vector is reconstructed four samples at a time in a 32-bit word whose
/// odd and even bytes are summed in two 16-bit lanes, and the clip back to 0-255 is a bit trick on those
/// lanes. A lane that goes negative borrows from its neighbour, so the reconstruction is not the clamped
/// per-sample sum in every case; the word arithmetic is kept exactly as written, because that is what
/// every SVQ1 decoder in use computes.
/// </remarks>
internal sealed class Svq1Decoder {

  internal const int BlockSkip = 0;
  internal const int BlockInter = 1;
  internal const int BlockInter4V = 2;
  internal const int BlockIntra = 3;

  private static readonly byte[] _StringTable = [
    0x00, 0xD5, 0x7F, 0xAA, 0xFE, 0x2B, 0x81, 0x54, 0x29, 0xFC, 0x56, 0x83, 0xD7, 0x02, 0xA8, 0x7D,
    0x52, 0x87, 0x2D, 0xF8, 0xAC, 0x79, 0xD3, 0x06, 0x7B, 0xAE, 0x04, 0xD1, 0x85, 0x50, 0xFA, 0x2F,
    0xA4, 0x71, 0xDB, 0x0E, 0x5A, 0x8F, 0x25, 0xF0, 0x8D, 0x58, 0xF2, 0x27, 0x73, 0xA6, 0x0C, 0xD9,
    0xF6, 0x23, 0x89, 0x5C, 0x08, 0xDD, 0x77, 0xA2, 0xDF, 0x0A, 0xA0, 0x75, 0x21, 0xF4, 0x5E, 0x8B,
    0x9D, 0x48, 0xE2, 0x37, 0x63, 0xB6, 0x1C, 0xC9, 0xB4, 0x61, 0xCB, 0x1E, 0x4A, 0x9F, 0x35, 0xE0,
    0xCF, 0x1A, 0xB0, 0x65, 0x31, 0xE4, 0x4E, 0x9B, 0xE6, 0x33, 0x99, 0x4C, 0x18, 0xCD, 0x67, 0xB2,
    0x39, 0xEC, 0x46, 0x93, 0xC7, 0x12, 0xB8, 0x6D, 0x10, 0xC5, 0x6F, 0xBA, 0xEE, 0x3B, 0x91, 0x44,
    0x6B, 0xBE, 0x14, 0xC1, 0x95, 0x40, 0xEA, 0x3F, 0x42, 0x97, 0x3D, 0xE8, 0xBC, 0x69, 0xC3, 0x16,
    0xEF, 0x3A, 0x90, 0x45, 0x11, 0xC4, 0x6E, 0xBB, 0xC6, 0x13, 0xB9, 0x6C, 0x38, 0xED, 0x47, 0x92,
    0xBD, 0x68, 0xC2, 0x17, 0x43, 0x96, 0x3C, 0xE9, 0x94, 0x41, 0xEB, 0x3E, 0x6A, 0xBF, 0x15, 0xC0,
    0x4B, 0x9E, 0x34, 0xE1, 0xB5, 0x60, 0xCA, 0x1F, 0x62, 0xB7, 0x1D, 0xC8, 0x9C, 0x49, 0xE3, 0x36,
    0x19, 0xCC, 0x66, 0xB3, 0xE7, 0x32, 0x98, 0x4D, 0x30, 0xE5, 0x4F, 0x9A, 0xCE, 0x1B, 0xB1, 0x64,
    0x72, 0xA7, 0x0D, 0xD8, 0x8C, 0x59, 0xF3, 0x26, 0x5B, 0x8E, 0x24, 0xF1, 0xA5, 0x70, 0xDA, 0x0F,
    0x20, 0xF5, 0x5F, 0x8A, 0xDE, 0x0B, 0xA1, 0x74, 0x09, 0xDC, 0x76, 0xA3, 0xF7, 0x22, 0x88, 0x5D,
    0xD6, 0x03, 0xA9, 0x7C, 0x28, 0xFD, 0x57, 0x82, 0xFF, 0x2A, 0x80, 0x55, 0x01, 0xD4, 0x7E, 0xAB,
    0x84, 0x51, 0xFB, 0x2E, 0x7A, 0xAF, 0x05, 0xD0, 0xAD, 0x78, 0xD2, 0x07, 0x53, 0x86, 0x2C, 0xF9,
  ];

  internal static readonly Svq1Vlc BlockType = new(Svq1Tables.BlockTypeCodes);
  internal static readonly Svq1Vlc Motion = new(Svq1Tables.MotionCodes);
  internal static readonly Svq1Vlc IntraMean = new(Svq1Tables.IntraMeanCodes);
  internal static readonly Svq1Vlc InterMean = new(Svq1Tables.InterMeanCodes);
  internal static readonly Svq1Vlc[] IntraMultistage = Array.ConvertAll(Svq1Tables.IntraMultistageCodes, static c => new Svq1Vlc(c));
  internal static readonly Svq1Vlc[] InterMultistage = Array.ConvertAll(Svq1Tables.InterMultistageCodes, static c => new Svq1Vlc(c));

  // Codebooks as the little-endian words FFmpeg reads them as.
  private static readonly uint[][] _IntraWords = Array.ConvertAll(Svq1Tables.IntraCodebooks, _Words);
  private static readonly uint[][] _InterWords = Array.ConvertAll(Svq1Tables.InterCodebooks, _Words);

  private static uint[] _Words(sbyte[] bytes) {
    var words = new uint[bytes.Length / 4];
    for (var i = 0; i < words.Length; ++i)
      words[i] = (uint)(byte)bytes[i * 4] | (uint)(byte)bytes[i * 4 + 1] << 8 | (uint)(byte)bytes[i * 4 + 2] << 16 | (uint)(byte)bytes[i * 4 + 3] << 24;
    return words;
  }

  private Plane[]? _previous;
  private int _lastTemporalReference = 0xFF;
  private readonly bool _hasExtraData;

  internal Svq1Decoder(int width, int height, bool hasExtraData) {
    this.Width = (width + 3) & ~3;
    this.Height = (height + 3) & ~3;
    this._hasExtraData = hasExtraData;
  }

  /// <summary>The last reference picture decoded, which the next predicted frame reads from.</summary>
  internal Plane[]? LastPlanes => this._previous;

  internal int Width { get; private set; }
  internal int Height { get; private set; }

  /// <summary>One plane at the size SVQ1 codes it: a whole number of 16x16 blocks.</summary>
  internal sealed class Plane(int width, int height) {
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly byte[] Samples = new byte[width * height];
  }

  /// <summary>Decodes one packet into Y, U and V at their coded sizes.</summary>
  internal Plane[] Decode(byte[] packet) {
    var bits = new Svq1BitReader(packet);
    var frameCode = (int)bits.ReadBits(22);
    if ((frameCode & ~0x70) != 0 || (frameCode & 0x60) == 0)
      throw new InvalidDataException($"An SVQ1 frame starts with the invalid frame code 0x{frameCode:X}.");

    if (frameCode != 0x20) {
      // The first sixteen bytes after the first four are stored scrambled; FFmpeg undoes it on a copy.
      if (packet.Length < 9 * 4)
        throw new InvalidDataException("An SVQ1 frame is too short for its scrambled header.");
      packet = (byte[])packet.Clone();
      var words = new uint[8];
      for (var i = 0; i < 8; ++i)
        words[i] = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4 + i * 4));
      for (var i = 0; i < 4; ++i)
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4 + i * 4), ((words[i] << 16) | (words[i] >> 16)) ^ words[7 - i]);
      bits = new(packet);
      bits.Skip(22);
    }

    var (intra, nonReference, buggy) = this._ReadHeader(ref bits, frameCode);

    var alignedWidth = (this.Width + 15) & ~15;
    var alignedHeight = (this.Height + 15) & ~15;
    if (bits.BitsLeft < alignedWidth * alignedHeight / 256)
      throw new InvalidDataException("An SVQ1 frame is too short for its picture.");

    var planes = new Plane[3];
    for (var i = 0; i < 3; ++i) {
      var width = i == 0 ? alignedWidth : ((this.Width / 4) + 15) & ~15;
      var height = i == 0 ? alignedHeight : ((this.Height / 4) + 15) & ~15;
      planes[i] = new(width, height);
    }

    if (!intra && (this._previous == null || this._previous[0].Width != planes[0].Width || this._previous[0].Height != planes[0].Height))
      throw new InvalidDataException("An SVQ1 inter frame has no reference picture of its size to predict from.");

    var motion = new MotionVector[alignedWidth / 8 + 3];
    for (var i = 0; i < 3; ++i) {
      var plane = planes[i];
      if (intra) {
        for (var y = 0; y < plane.Height; y += 16)
        for (var x = 0; x < plane.Width; x += 16)
          _DecodeIntraBlock(ref bits, plane.Samples, y * plane.Width + x, plane.Width);
      } else {
        var previous = this._previous![i];
        Array.Clear(motion);
        for (var y = 0; y < plane.Height; y += 16) {
          for (var x = 0; x < plane.Width; x += 16)
            _DecodeDeltaBlock(ref bits, plane, previous, motion, x, y, buggy);
          motion[0] = default;
        }
      }
    }

    if (!nonReference)
      this._previous = planes;
    return planes;
  }

  private (bool Intra, bool NonReference, bool Buggy) _ReadHeader(ref Svq1BitReader bits, int frameCode) {
    var temporalReference = (int)bits.ReadBits(8);
    var buggy = temporalReference == 0 && this._lastTemporalReference == 0 && !this._hasExtraData;
    this._lastTemporalReference = temporalReference;

    var type = (int)bits.ReadBits(2);
    if (type == 3)
      throw new InvalidDataException("An SVQ1 frame names the invalid frame type 3.");
    var intra = type == 0;
    var nonReference = type == 2;

    if (intra) {
      if (frameCode is 0x50 or 0x60)
        bits.Skip(16); // the checksum FFmpeg computes and only logs

      if ((frameCode ^ 0x10) >= 0x50) {
        // An embedded message, scrambled through the string table.
        var length = (int)bits.ReadBits(8);
        int seed = _StringTable[length];
        for (var i = 1; i <= length; ++i) {
          var value = (int)bits.ReadBits(8) ^ seed;
          seed = _StringTable[value ^ seed];
        }
      }

      bits.Skip(5);
      var sizeCode = (int)bits.ReadBits(3);
      int width, height;
      if (sizeCode == 7) {
        width = (int)bits.ReadBits(12);
        height = (int)bits.ReadBits(12);
        if (width == 0 || height == 0)
          throw new InvalidDataException("An SVQ1 key frame states a picture with no area.");
      } else {
        (width, height) = Svq1Tables.FrameSizes[sizeCode];
      }
      this.Width = width;
      this.Height = height;
    }

    if (bits.ReadBit() != 0) {
      bits.Skip(2);
      if (bits.ReadBits(2) != 0)
        throw new InvalidDataException("An SVQ1 frame states reserved checksum options.");
    }

    if (bits.ReadBit() != 0) {
      bits.Skip(1 + 4 + 1 + 2);
      // FFmpeg's skip_1stop_8data_bits: while a 1 bit, skip 8 bits.
      while (bits.ReadBit() != 0) {
        if (bits.BitsLeft <= 0)
          throw new InvalidDataException("An SVQ1 frame's extra header data runs past its end.");
        bits.Skip(8);
      }
    }

    if (bits.BitsLeft <= 0)
      throw new InvalidDataException("An SVQ1 frame ends in its header.");
    return (intra, nonReference, buggy);
  }

  // ==============================================================================================
  // Blocks
  // ==============================================================================================

  private static void _DecodeDeltaBlock(
    ref Svq1BitReader bits, Plane current, Plane previous, MotionVector[] motion, int x, int y, bool buggy) {
    var blockType = BlockType.Read(ref bits);
    if (blockType < 0)
      throw new InvalidDataException("An SVQ1 block type code names nothing.");

    if (blockType is BlockSkip or BlockIntra) {
      motion[0] = default;
      motion[x / 8 + 2] = default;
      motion[x / 8 + 3] = default;
    }

    var offset = y * current.Width + x;
    switch (blockType) {
      case BlockSkip:
        for (var r = 0; r < 16; ++r)
          previous.Samples.AsSpan((y + r) * previous.Width + x, 16).CopyTo(current.Samples.AsSpan(offset + r * current.Width));
        break;
      case BlockInter:
        _MotionInter(ref bits, current, previous, motion, x, y);
        _DecodeInterBlock(ref bits, current.Samples, offset, current.Width, buggy);
        break;
      case BlockInter4V:
        _MotionInter4V(ref bits, current, previous, motion, x, y);
        _DecodeInterBlock(ref bits, current.Samples, offset, current.Width, buggy);
        break;
      default:
        _DecodeIntraBlock(ref bits, current.Samples, offset, current.Width);
        break;
    }
  }

  internal record struct MotionVector(int X, int Y);

  private static MotionVector _ReadMotion(ref Svq1BitReader bits, MotionVector p0, MotionVector p1, MotionVector p2) {
    int x = 0, y = 0;
    for (var i = 0; i < 2; ++i) {
      var diff = Motion.Read(ref bits);
      if (diff < 0)
        throw new InvalidDataException("An SVQ1 motion vector code names nothing.");
      if (diff != 0 && bits.ReadBit() != 0)
        diff = -diff;

      if (i == 1)
        y = _SignExtend6(diff + _Median(p0.Y, p1.Y, p2.Y));
      else
        x = _SignExtend6(diff + _Median(p0.X, p1.X, p2.X));
    }
    return new(x, y);
  }

  private static int _SignExtend6(int value) => (value << 26) >> 26;

  internal static int _Median(int a, int b, int c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

  private static void _MotionInter(ref Svq1BitReader bits, Plane current, Plane previous, MotionVector[] motion, int x, int y) {
    var p0 = motion[0];
    var (p1, p2) = y == 0 ? (p0, p0) : (motion[x / 8 + 2], motion[x / 8 + 4]);
    var mv = _ReadMotion(ref bits, p0, p1, p2);
    motion[0] = mv;
    motion[x / 8 + 2] = mv;
    motion[x / 8 + 3] = mv;

    var mx = Math.Clamp(mv.X, -2 * x, 2 * (current.Width - x - 16));
    var my = Math.Clamp(mv.Y, -2 * y, 2 * (current.Height - y - 16));
    PutPixels(previous, x + (mx >> 1), y + (my >> 1), (mx & 1) | ((my & 1) << 1), current, x, y, 16);
  }

  private static void _MotionInter4V(ref Svq1BitReader bits, Plane current, Plane previous, MotionVector[] motion, int x, int y) {
    var index = x / 8;
    var pmv = new MotionVector[4];

    var p0 = motion[0];
    var (p1, p2) = y == 0 ? (p0, p0) : (motion[index + 2], motion[index + 4]);
    var mv = _ReadMotion(ref bits, p0, p1, p2);

    // Vector 1 predicts from vector 0; FFmpeg keeps its third predictor from before.
    var q1 = y == 0 ? mv : motion[index + 3];
    var q2 = y == 0 ? mv : p2;
    motion[0] = _ReadMotion(ref bits, mv, q1, q2);

    motion[index + 2] = _ReadMotion(ref bits, mv, motion[0], motion[index + 1]);
    motion[index + 3] = _ReadMotion(ref bits, mv, motion[0], motion[index + 2]);

    pmv[0] = mv;
    pmv[1] = motion[0];
    pmv[2] = motion[index + 2];
    pmv[3] = motion[index + 3];

    for (var i = 0; i < 4; ++i) {
      var mvx = pmv[i].X + (i & 1) * 16;
      var mvy = pmv[i].Y + (i >> 1) * 16;
      mvx = Math.Clamp(mvx, -2 * x, 2 * (current.Width - x - 8));
      mvy = Math.Clamp(mvy, -2 * y, 2 * (current.Height - y - 8));
      var dx = x + (i & 1) * 8;
      var dy = y + (i >> 1) * 8;
      PutPixels(previous, x + (mvx >> 1), y + (mvy >> 1), (mvx & 1) | ((mvy & 1) << 1), current, dx, dy, 8);
    }
  }

  /// <summary>FFmpeg's <c>put_pixels_tab</c>: copy, or a rounded half-sample average across, down or both.</summary>
  internal static void PutPixels(Plane source, int sx, int sy, int halfSample, Plane destination, int dx, int dy, int size) {
    var s = source.Samples;
    var w = source.Width;
    for (var r = 0; r < size; ++r)
    for (var c = 0; c < size; ++c) {
      var at = (sy + r) * w + sx + c;
      int value = halfSample switch {
        0 => s[at],
        1 => (s[at] + s[at + 1] + 1) >> 1,
        2 => (s[at] + s[at + w] + 1) >> 1,
        _ => (s[at] + s[at + 1] + s[at + w] + s[at + w + 1] + 2) >> 2,
      };
      destination.Samples[(dy + r) * destination.Width + dx + c] = (byte)value;
    }
  }

  /// <summary>
  /// FFmpeg's <c>SVQ1_PROCESS_VECTOR</c>: the breadth-first walk over the quadtree, one split bit per
  /// vector, returning the vectors to reconstruct as (offset, level) in decode order.
  /// </summary>
  private static IEnumerable<(int Offset, int Level)> _Vectors(Svq1BitReaderBox bits, int pixels, int pitch) {
    var list = new int[63];
    list[0] = pixels;
    int i = 0, m = 1, n = 1, level = 5;
    for (; i < n; ++i) {
      while (level > 0) {
        if (i == m) {
          m = n;
          if (--level == 0)
            break;
        }
        if (bits.ReadBit() == 0)
          break;
        list[n++] = list[i];
        list[n++] = list[i] + (((level & 1) != 0 ? pitch : 1) << ((level >> 1) + 1));
        ++i;
      }
      yield return (list[i], level);
    }
  }

  /// <summary>A heap box for the bit reader so the quadtree walk can be an iterator.</summary>
  private sealed class Svq1BitReaderBox(Svq1BitReader reader) {
    public Svq1BitReader Reader = reader;
    public int ReadBit() => this.Reader.ReadBit();
  }

  private static void _DecodeIntraBlock(ref Svq1BitReader bits, byte[] pixels, int origin, int pitch) {
    var box = new Svq1BitReaderBox(bits);
    foreach (var (offset, level) in _Vectors(box, origin, pitch)) {
      var width = 1 << ((4 + level) / 2);
      var height = 1 << ((3 + level) / 2);
      var stages = IntraMultistage[level].Read(ref box.Reader) - 1;
      if (stages < -1)
        throw new InvalidDataException("An SVQ1 stage-count code names nothing.");

      if (stages == -1) {
        for (var y = 0; y < height; ++y)
          pixels.AsSpan(offset + y * pitch, width).Clear();
        continue;
      }

      if (stages > 0 && level >= 4)
        throw new InvalidDataException("An SVQ1 vector of level 4 or 5 cannot have codebook stages.");

      var mean = IntraMean.Read(ref box.Reader);
      if (mean < 0)
        throw new InvalidDataException("An SVQ1 mean code names nothing.");

      if (stages == 0) {
        for (var y = 0; y < height; ++y)
          pixels.AsSpan(offset + y * pitch, width).Fill((byte)mean);
        continue;
      }

      _Reconstruct(ref box.Reader, pixels, offset, pitch, level, width, height, stages, (uint)mean, _IntraWords[level], inter: false);
    }
    bits = box.Reader;
  }

  private static void _DecodeInterBlock(ref Svq1BitReader bits, byte[] pixels, int origin, int pitch, bool buggy) {
    var box = new Svq1BitReaderBox(bits);
    foreach (var (offset, level) in _Vectors(box, origin, pitch)) {
      var width = 1 << ((4 + level) / 2);
      var height = 1 << ((3 + level) / 2);
      var stages = InterMultistage[level].Read(ref box.Reader) - 1;
      if (stages < -1)
        throw new InvalidDataException("An SVQ1 stage-count code names nothing.");
      if (stages == -1)
        continue;
      if (stages > 0 && level >= 4)
        throw new InvalidDataException("An SVQ1 vector of level 4 or 5 cannot have codebook stages.");

      var symbol = InterMean.Read(ref box.Reader);
      if (symbol < 0)
        throw new InvalidDataException("An SVQ1 mean code names nothing.");
      var mean = symbol - 256;
      if (buggy)
        mean = mean switch { -128 => 128, 128 => -128, _ => mean };

      _Reconstruct(ref box.Reader, pixels, offset, pitch, level, width, height, stages, unchecked((uint)mean),
        level < 4 ? _InterWords[level] : null, inter: true);
    }
    bits = box.Reader;
  }

  /// <summary>FFmpeg's <c>SVQ1_CALC_CODEBOOK_ENTRIES</c> and <c>SVQ1_ADD_CODEBOOK</c>, word for word.</summary>
  private static void _Reconstruct(
    ref Svq1BitReader bits, byte[] pixels, int offset, int pitch, int level, int width, int height, int stages,
    uint mean, uint[]? codebook, bool inter) {
    Span<int> entries = stackalloc int[6];
    if (stages > 0) {
      var cache = bits.ReadBits(4 * stages);
      for (var j = 0; j < stages; ++j)
        entries[j] = (int)((((cache >> (4 * (stages - j - 1))) & 0xF) + 16 * (uint)j) << (level + 1));
    }

    mean = unchecked(mean - (uint)stages * 128);
    var n4 = unchecked((mean << 16) + mean);
    var book = 0;

    for (var y = 0; y < height; ++y) {
      var row = offset + y * pitch;
      for (var x = 0; x < width / 4; ++x) {
        uint n1, n2, n3;
        if (inter) {
          n3 = BinaryPrimitives.ReadUInt32LittleEndian(pixels.AsSpan(row + x * 4));
          n1 = unchecked(n4 + ((n3 & 0xFF00FF00) >> 8));
          n2 = unchecked(n4 + (n3 & 0x00FF00FF));
        } else {
          n1 = n4;
          n2 = n4;
        }

        for (var j = 0; j < stages; ++j) {
          n3 = codebook![entries[j] + book] ^ 0x80808080;
          n1 = unchecked(n1 + ((n3 & 0xFF00FF00) >> 8));
          n2 = unchecked(n2 + (n3 & 0x00FF00FF));
        }

        if ((n1 & 0xFF00FF00) != 0) {
          n3 = unchecked((((n1 >> 15) & 0x00010001) | 0x01000100) - 0x00010001);
          n1 = unchecked(n1 + 0x7F007F00);
          n1 |= unchecked((((~n1 >> 15) & 0x00010001) | 0x01000100) - 0x00010001);
          n1 &= n3 & 0x00FF00FF;
        }
        if ((n2 & 0xFF00FF00) != 0) {
          n3 = unchecked((((n2 >> 15) & 0x00010001) | 0x01000100) - 0x00010001);
          n2 = unchecked(n2 + 0x7F007F00);
          n2 |= unchecked((((~n2 >> 15) & 0x00010001) | 0x01000100) - 0x00010001);
          n2 &= n3 & 0x00FF00FF;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(pixels.AsSpan(row + x * 4), (n1 << 8) | n2);
        if (codebook != null)
          ++book;
      }
    }
  }
}
