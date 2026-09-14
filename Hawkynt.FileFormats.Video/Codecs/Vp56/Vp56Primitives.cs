using System;
using System.IO;

namespace FileFormat.Codecs.Vp56;

/// <summary>VP5/VP6 reference-frame selector.</summary>
internal enum Vp56Reference : sbyte {
  None = -1,
  Current = 0,
  Previous = 1,
  Golden = 2,
}

/// <summary>The ten macroblock coding modes shared by VP5 and VP6.</summary>
internal enum Vp56MacroblockType : byte {
  InterNoVectorPrevious = 0,
  Intra = 1,
  InterDeltaPrevious = 2,
  InterVector1Previous = 3,
  InterVector2Previous = 4,
  InterNoVectorGolden = 5,
  InterDeltaGolden = 6,
  InterFourVectors = 7,
  InterVector1Golden = 8,
  InterVector2Golden = 9,
}

internal readonly record struct Vp56MotionVector(short X, short Y) {
  internal static readonly Vp56MotionVector Zero = new(0, 0);

  internal static Vp56MotionVector operator +(Vp56MotionVector left, Vp56MotionVector right)
    => new((short)(left.X + right.X), (short)(left.Y + right.Y));
}

internal readonly record struct Vp56TreeNode(sbyte Value, byte ProbabilityIndex = 0);

internal struct Vp56Macroblock {
  internal Vp56MacroblockType Type;
  internal Vp56MotionVector MotionVector;
}

internal struct Vp56DcPredictor {
  internal byte NotNullDc;
  internal Vp56Reference Reference;
  internal short Dc;
}

/// <summary>
/// The VPx binary arithmetic decoder used by VP5 and VP6.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's LGPL-2.1-or-later <c>libavcodec/vpx_rac.h</c>; the arithmetic is also
/// specified by On2's VP6 Bitstream &amp; Decoder Specification §7.3. The probability is the chance
/// of a zero out of 256. Missing bytes are zero-filled only while flushing the coder; callers can
/// inspect <see cref="OverreadBytes"/> to reject a structurally truncated partition.
/// </remarks>
internal struct Vp56RangeDecoder {
  private readonly ReadOnlyMemory<byte> _data;
  private readonly int _end;
  private int _position;
  private uint _range;
  private uint _value;
  private int _bitCount;

  internal Vp56RangeDecoder(ReadOnlyMemory<byte> data, int start = 0, int? length = null) {
    var actualLength = length ?? (data.Length - start);
    if ((uint)start > (uint)data.Length || actualLength < 0 || start + actualLength > data.Length)
      throw new InvalidDataException("VP5/VP6 range-coder partition lies outside the coded packet.");

    this._data = data;
    this._end = start + actualLength;
    var span = data.Span;
    this._value = ((start < this._end ? (uint)span[start] : 0u) << 8)
      | (start + 1 < this._end ? span[start + 1] : 0u);
    this._position = start + 2;
    this._range = 255;
    this._bitCount = 0;
  }

  internal int OverreadBytes => Math.Max(0, this._position - this._end);

  internal int ReadBool(int probability) {
    if ((uint)probability > 255u)
      throw new ArgumentOutOfRangeException(nameof(probability));

    var split = 1 + (((this._range - 1) * (uint)probability) >> 8);
    var bigSplit = split << 8;
    int result;
    if (this._value >= bigSplit) {
      result = 1;
      this._range -= split;
      this._value -= bigSplit;
    } else {
      result = 0;
      this._range = split;
    }

    while (this._range < 128) {
      this._range <<= 1;
      this._value <<= 1;
      if (++this._bitCount != 8)
        continue;

      this._bitCount = 0;
      var span = this._data.Span;
      if (this._position < this._end)
        this._value |= span[this._position];
      ++this._position;
    }

    return result;
  }

  internal int ReadFlag() => this.ReadBool(128);

  internal int ReadLiteral(int bits) {
    var result = 0;
    while (bits-- > 0)
      result = (result << 1) | this.ReadFlag();
    return result;
  }

  /// <summary>Reads a P(7) VP56 probability: seven bits, doubled, with zero represented by one.</summary>
  internal byte ReadProbability() {
    var value = this.ReadLiteral(7) << 1;
    return (byte)(value == 0 ? 1 : value);
  }

  internal int ReadTree(ReadOnlySpan<Vp56TreeNode> tree, ReadOnlySpan<byte> probabilities) {
    var index = 0;
    while (tree[index].Value > 0) {
      var node = tree[index];
      index += this.ReadBool(probabilities[node.ProbabilityIndex]) != 0 ? node.Value : 1;
      if ((uint)index >= (uint)tree.Length)
        throw new InvalidDataException("VP5/VP6 entropy tree escaped its table.");
    }

    return -tree[index].Value;
  }
}

/// <summary>MSB-first bit reader used by VP6's optional Huffman coefficient partition.</summary>
internal ref struct Vp56BitReader {
  private readonly ReadOnlySpan<byte> _data;
  private int _position;

  internal Vp56BitReader(ReadOnlySpan<byte> data) => this._data = data;

  internal int BitsLeft => this._data.Length * 8 - this._position;

  internal int ReadBit() {
    if (this._position >= this._data.Length * 8)
      throw new InvalidDataException("VP6 Huffman coefficient partition ended in a codeword.");
    var bit = (this._data[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;
    return bit;
  }

  internal int ReadBits(int count) {
    if ((uint)count > 31u || count > this.BitsLeft)
      throw new InvalidDataException("VP6 Huffman coefficient partition is truncated.");
    var value = 0;
    while (count-- > 0)
      value = (value << 1) | this.ReadBit();
    return value;
  }
}

/// <summary>A padded planar 4:2:0 reconstruction frame in coded (not display-flipped) row order.</summary>
internal sealed class Vp56Frame {
  internal Vp56Frame(int macroblockWidth, int macroblockHeight) {
    this.MacroblockWidth = macroblockWidth;
    this.MacroblockHeight = macroblockHeight;
    this.LumaWidth = macroblockWidth * 16;
    this.LumaHeight = macroblockHeight * 16;
    this.ChromaWidth = macroblockWidth * 8;
    this.ChromaHeight = macroblockHeight * 8;
    this.Luma = new byte[this.LumaWidth * this.LumaHeight];
    this.Cb = new byte[this.ChromaWidth * this.ChromaHeight];
    this.Cr = new byte[this.ChromaWidth * this.ChromaHeight];
  }

  internal int MacroblockWidth { get; }
  internal int MacroblockHeight { get; }
  internal int LumaWidth { get; }
  internal int LumaHeight { get; }
  internal int ChromaWidth { get; }
  internal int ChromaHeight { get; }
  internal byte[] Luma { get; }
  internal byte[] Cb { get; }
  internal byte[] Cr { get; }

  internal byte[] Plane(int plane) => plane switch {
    0 => this.Luma,
    1 => this.Cb,
    2 => this.Cr,
    _ => throw new ArgumentOutOfRangeException(nameof(plane)),
  };

  internal int PlaneWidth(int plane) => plane == 0 ? this.LumaWidth : this.ChromaWidth;
  internal int PlaneHeight(int plane) => plane == 0 ? this.LumaHeight : this.ChromaHeight;

  internal void CopyFrom(Vp56Frame source) {
    source.Luma.AsSpan().CopyTo(this.Luma);
    source.Cb.AsSpan().CopyTo(this.Cb);
    source.Cr.AsSpan().CopyTo(this.Cr);
  }
}

/// <summary>Mutable entropy-model state carried between VP5/VP6 frames.</summary>
internal sealed class Vp56Model {
  internal readonly byte[] CoeffReorder = new byte[64];
  internal readonly byte[] CoeffIndexToPos = new byte[64];
  internal readonly byte[] CoeffIndexToIdctSelector = new byte[64];
  internal readonly byte[] VectorSign = new byte[2];
  internal readonly byte[] VectorDct = new byte[2];
  internal readonly byte[,] VectorPdi = new byte[2, 2];
  internal readonly byte[,] VectorPdv = new byte[2, 7];
  internal readonly byte[,] VectorFdv = new byte[2, 8];
  internal readonly byte[,] CoeffDccv = new byte[2, 11];
  internal readonly byte[,,,] CoeffRact = new byte[2, 3, 6, 11];
  internal readonly byte[,,,,] CoeffAcct = new byte[2, 3, 3, 6, 5];
  internal readonly byte[,,] CoeffDcct = new byte[2, 36, 5];
  internal readonly byte[,] CoeffRunv = new byte[2, 14];
  internal readonly byte[,,] MacroblockType = new byte[3, 10, 10];
  internal readonly byte[,,] MacroblockTypeStats = new byte[3, 10, 2];
}
