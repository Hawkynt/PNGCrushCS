using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Ylc;

/// <summary>
/// The YUY2 Lossless Codec both ways: four Huffman tables built from counts in the packet, a picture
/// of four-byte YUYV groups coded through them, and gradient prediction over the whole frame.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/ylc.c</c>, LGPL-2.1-or-later: <c>build_vlc</c> and
/// <c>get_tree_codes</c>, whose pairing of nodes and complemented codes are the format's and are
/// followed exactly, and <c>decode_frame</c>. The encoder is this package's own.
/// </remarks>
internal static class YlcCodec {

  private const uint _TAG = 0x30434C59; // "YLC0"
  private const int _MAX_CODE_LENGTH = 30;
  private const int _FIRST_RUN_SYMBOL = 0xE1;

  /// <summary>One codeword: <see cref="Length"/> bits, most significant first, standing for <see cref="Symbol"/>.</summary>
  internal readonly record struct Code(uint Bits, int Length, byte Symbol);

  /// <summary>FFmpeg's <c>build_vlc</c>: a Huffman tree paired the way the format pairs it, and its codes.</summary>
  internal static List<Code> BuildCodes(ReadOnlySpan<uint> counts) {
    var count = new uint[512];
    var symbol = new short[512];
    var left = new short[512];
    var right = new short[512];
    for (var i = 0; i < 256; ++i) {
      count[i] = counts[i];
      symbol[i] = (short)i;
      left[i] = right[i] = (short)i;
    }

    var current = 256;
    var j = 0;
    do {
      while (true) {
        var first = current;
        var second = current;
        count[current] = uint.MaxValue;
        for (var node = j; node != current; ++node) {
          var value = count[node];
          if (value != 0 && value < count[first]) {
            if (value >= count[second])
              first = node;
            else {
              first = second;
              second = node;
            }
          }
        }

        if (first == current)
          break;

        var sum = (ulong)count[second] + count[first];
        if (count[second] >= uint.MaxValue - count[first])
          throw new InvalidDataException("A YLC code table's counts overflow.");
        count[second] = 0;
        count[first] = 0;
        count[current] = (uint)sum;
        symbol[current] = -1;
        left[current] = (short)first;
        right[current] = (short)second;
        ++current;
      }
      ++j;
    } while (current - 256 == j);

    var codes = new List<Code>();
    _Walk(codes, symbol, left, right, current - 1, 0, 0);
    return codes;
  }

  private static void _Walk(List<Code> codes, short[] symbol, short[] left, short[] right, int node, ulong prefix, int length) {
    if (symbol[node] != -1) {
      var bits = Math.Max(length, 1);
      codes.Add(new((uint)(~prefix & ((1UL << bits) - 1)), bits, (byte)(symbol[node] + (length == 0 ? 1 : 0))));
      return;
    }

    prefix <<= 1;
    ++length;
    _Walk(codes, symbol, left, right, left[node], prefix, length);
    _Walk(codes, symbol, left, right, right[node], prefix | 1, length);
  }

  // ============================================================================================
  // Bits
  // ============================================================================================

  /// <summary>
  /// A packet region as FFmpeg reads it: its whole 32-bit words byte-swapped, then read most
  /// significant bit first.
  /// </summary>
  private sealed class Reader {
    private readonly byte[] _bytes;
    private int _bit;

    internal Reader(ReadOnlySpan<byte> region, bool swapPartialWord) {
      var words = swapPartialWord ? (region.Length + 3) >> 2 : region.Length >> 2;
      this._bytes = new byte[Math.Max(region.Length, words * 4) + 8];
      region.CopyTo(this._bytes);
      for (var w = 0; w < words; ++w)
        Array.Reverse(this._bytes, w * 4, 4);
      this.Length = region.Length * 8;
    }

    internal int Length { get; }

    internal int Left => this.Length - this._bit;

    internal int ReadBit() {
      var value = (this._bytes[this._bit >> 3] >> (7 - (this._bit & 7))) & 1;
      ++this._bit;
      return value;
    }

    internal uint ReadBits(int count) {
      uint value = 0;
      for (var i = 0; i < count; ++i)
        value = (value << 1) | (uint)this.ReadBit();
      return value;
    }

    internal int ReadCode(Dictionary<(int, uint), byte> codes) {
      uint bits = 0;
      for (var length = 1; length <= _MAX_CODE_LENGTH; ++length) {
        bits = (bits << 1) | (uint)this.ReadBit();
        if (codes.TryGetValue((length, bits), out var symbol))
          return symbol;
      }
      return -1;
    }
  }

  internal sealed class Writer {
    private readonly List<byte> _bytes = [];
    private int _current;
    private int _filled;

    internal void Write(uint value, int count) {
      for (var i = count - 1; i >= 0; --i) {
        this._current = (this._current << 1) | (int)((value >> i) & 1);
        if (++this._filled == 8) {
          this._bytes.Add((byte)this._current);
          this._current = 0;
          this._filled = 0;
        }
      }
    }

    /// <summary>The bytes, padded to whole 32-bit words and each word byte-swapped, as the decoder reads them.</summary>
    internal byte[] ToSwappedWords() {
      var bytes = new List<byte>(this._bytes);
      if (this._filled != 0)
        bytes.Add((byte)(this._current << (8 - this._filled)));
      for (var i = 0; i < 4; ++i)
        bytes.Add(0);
      while ((bytes.Count & 3) != 0)
        bytes.Add(0);
      var result = bytes.ToArray();
      for (var w = 0; w < result.Length / 4; ++w)
        Array.Reverse(result, w * 4, 4);
      return result;
    }
  }

  // ============================================================================================
  // Decoding
  // ============================================================================================

  /// <summary>Decodes one packet into a packed YUYV picture.</summary>
  internal static byte[] Decode(ReadOnlySpan<byte> packet, int width, int height) {
    if (packet.Length <= 16)
      throw new InvalidDataException($"A YLC packet holds a sixteen-byte header and more; this one is {packet.Length} bytes.");
    if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != _TAG || BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]) != 0)
      throw new InvalidDataException("A YLC packet does not open with YLC0 and a zero word.");
    var tables = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]);
    var data = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
    if (tables < 16 || tables >= packet.Length || tables >= data || data >= packet.Length)
      throw new InvalidDataException("A YLC packet states its tables or its picture outside itself.");

    var reader = new Reader(packet[tables..data], swapPartialWord: true);
    var lookups = new Dictionary<(int, uint), byte>[4];
    Span<uint> counts = stackalloc uint[256];
    for (var t = 0; t < 4; ++t) {
      for (var x = 0; x < 256; ++x) {
        var length = 0;
        while (length < 31 && reader.ReadBit() != 1)
          ++length;
        counts[x] = ((1u << length) - 1) + reader.ReadBits(length);
      }
      lookups[t] = [];
      foreach (var code in BuildCodes(counts))
        lookups[t][(code.Length, code.Bits)] = code.Symbol;
    }

    var stride = width * 2;
    var picture = new byte[stride * height];
    var bits = new Reader(packet[data..], swapPartialWord: false);
    for (int y = 0, row = 0; y < height; ++y, row += stride)
      for (var x = 0; x < stride && y < height;) {
        if (bits.Left <= 0)
          throw new InvalidDataException("A YLC picture's codes run out before the picture does.");

        if (bits.ReadBit() == 1) {
          var value = bits.ReadCode(lookups[0]);
          if (value < 0)
            throw new InvalidDataException("A YLC picture holds a code its first table does not.");
          if (value < _FIRST_RUN_SYMBOL) {
            picture[row + x] = YlcTables.FirstLuma[value];
            picture[row + x + 1] = YlcTables.Blue[value];
            picture[row + x + 2] = YlcTables.SecondLuma[value];
            picture[row + x + 3] = YlcTables.Red[value];
            x += 4;
          } else {
            // A run of groups left as they are: zero residue.
            var skip = (value - 0xDF) * 4;
            if (x + skip >= stride) {
              var rows = (x + skip) / stride;
              x = (x + skip) % stride;
              y += rows;
              row += rows * stride;
            } else
              x += skip;
          }
        } else {
          var y1 = bits.ReadCode(lookups[1]);
          var u = bits.ReadCode(lookups[2]);
          var y2 = bits.ReadCode(lookups[1]);
          var v = bits.ReadCode(lookups[3]);
          if (y1 < 0 || y2 < 0 || u < 0 || v < 0)
            throw new InvalidDataException("A YLC picture holds a code its tables do not.");
          if (row + x + 3 >= picture.Length)
            break;
          picture[row + x] = (byte)y1;
          picture[row + x + 1] = (byte)u;
          picture[row + x + 2] = (byte)(y1 + y2);
          picture[row + x + 3] = (byte)v;
          x += 4;
        }
      }

    _Predict(picture, stride, height);
    return picture;
  }

  /// <summary>Gradient prediction over the whole frame, the left and top-left values carried row to row.</summary>
  private static void _Predict(byte[] picture, int stride, int height) {
    int l0 = 128, l1 = 128, l2 = 128;
    int t0 = 128, t1 = 128, t2 = 128;
    for (var x = 0; x < stride; x += 4) {
      picture[x] = (byte)(picture[x] + l0);
      picture[x + 2] = (byte)(l0 = picture[x + 2] + l0);
      picture[x + 1] = (byte)(l1 = picture[x + 1] + l1);
      picture[x + 3] = (byte)(l2 = picture[x + 3] + l2);
    }

    for (var y = 1; y < height; ++y) {
      var row = y * stride;
      var above = row - stride;
      for (var x = 0; x < stride; x += 4) {
        picture[row + x] = (byte)(picture[row + x] + l0 + picture[above + x] - t0);
        picture[row + x + 2] = (byte)(l0 = picture[row + x + 2] + l0 + picture[above + x + 2] - t0);
        t0 = picture[above + x + 2];
        picture[row + x + 1] = (byte)(l1 = picture[row + x + 1] + l1 + picture[above + x + 1] - t1);
        t1 = picture[above + x + 1];
        picture[row + x + 3] = (byte)(l2 = picture[row + x + 3] + l2 + picture[above + x + 3] - t2);
        t2 = picture[above + x + 3];
      }
    }
  }

  // ============================================================================================
  // Encoding
  // ============================================================================================

  /// <summary>
  /// Codes a packed YUYV picture: a group of four zero residues in a run of two or more is skipped,
  /// a group the first table's residue tables hold is written as that symbol, and any other as four
  /// literal codes.
  /// </summary>
  internal static byte[] Encode(ReadOnlySpan<byte> picture, int width, int height) {
    var stride = width * 2;
    var residues = _Residues(picture, stride, height);
    var groups = residues.Length / 4;

    var table0 = new Dictionary<uint, byte>();
    for (var i = 0; i < _FIRST_RUN_SYMBOL; ++i) {
      var key = (uint)(YlcTables.FirstLuma[i] | (YlcTables.Blue[i] << 8) | (YlcTables.SecondLuma[i] << 16) | (YlcTables.Red[i] << 24));
      table0.TryAdd(key, (byte)i);
    }

    // The symbol stream, then the counts the tables are built from.
    var stream = new List<(int Table, byte Symbol)>();
    var counts = new uint[4, 256];
    for (var g = 0; g < groups;) {
      var at = g * 4;
      var key = BinaryPrimitives.ReadUInt32LittleEndian(residues.AsSpan(at));
      if (key == 0) {
        var run = 1;
        while (g + run < groups && run < 32 && BinaryPrimitives.ReadUInt32LittleEndian(residues.AsSpan((g + run) * 4)) == 0)
          ++run;
        if (run >= 2) {
          var symbol = (byte)(0xDF + run);
          stream.Add((0, symbol));
          ++counts[0, symbol];
          g += run;
          continue;
        }
      }

      if (table0.TryGetValue(key, out var index)) {
        stream.Add((0, index));
        ++counts[0, index];
      } else {
        var y1 = residues[at];
        var y2 = (byte)(residues[at + 2] - y1);
        stream.Add((1, y1));
        stream.Add((2, residues[at + 1]));
        stream.Add((1, y2));
        stream.Add((3, residues[at + 3]));
        ++counts[1, y1];
        ++counts[2, residues[at + 1]];
        ++counts[1, y2];
        ++counts[3, residues[at + 3]];
      }
      ++g;
    }

    var tables = new Writer();
    var lookup = new Dictionary<byte, Code>[4];
    Span<uint> row = stackalloc uint[256];
    for (var t = 0; t < 4; ++t) {
      for (var s = 0; s < 256; ++s)
        row[s] = counts[t, s];
      var codes = _FittingCodes(row);
      lookup[t] = [];
      foreach (var code in codes)
        lookup[t][code.Symbol] = code;
      for (var s = 0; s < 256; ++s)
        _WriteCount(tables, row[s]);
    }

    var bits = new Writer();
    for (var i = 0; i < stream.Count;) {
      var (table, symbol) = stream[i];
      if (table == 0) {
        bits.Write(1, 1);
        var code = lookup[0][symbol];
        bits.Write(code.Bits, code.Length);
        ++i;
        continue;
      }

      bits.Write(0, 1);
      for (var k = 0; k < 4; ++k, ++i) {
        var code = lookup[stream[i].Table][stream[i].Symbol];
        bits.Write(code.Bits, code.Length);
      }
    }

    var tableBytes = tables.ToSwappedWords();
    var dataBytes = bits.ToSwappedWords();
    var packet = new byte[16 + tableBytes.Length + dataBytes.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(packet, _TAG);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), (uint)(16 + tableBytes.Length));
    tableBytes.CopyTo(packet, 16);
    dataBytes.CopyTo(packet, 16 + tableBytes.Length);
    return packet;
  }

  /// <summary>
  /// Codes for the counts, which are flattened — the smaller ones raised towards the larger — until
  /// no code is longer than the decoder can read. The counts are changed in place to the ones written.
  /// </summary>
  private static List<Code> _FittingCodes(Span<uint> counts) {
    while (true) {
      var codes = BuildCodes(counts);
      var longest = 0;
      foreach (var code in codes)
        longest = Math.Max(longest, code.Length);
      if (longest <= _MAX_CODE_LENGTH)
        return codes;
      for (var s = 0; s < 256; ++s)
        if (counts[s] != 0)
          counts[s] = (counts[s] >> 1) | 1;
    }
  }

  private static void _WriteCount(Writer writer, uint value) {
    var length = 0;
    while (length < 31 && (1UL << (length + 1)) - 1 <= value)
      ++length;
    writer.Write(0, length);
    if (length < 31)
      writer.Write(1, 1);
    writer.Write((uint)(value - ((1UL << length) - 1)), length);
  }

  /// <summary>The bytes the decoder's prediction turns back into the picture.</summary>
  private static byte[] _Residues(ReadOnlySpan<byte> picture, int stride, int height) {
    var residues = new byte[stride * height];
    int l0 = 128, l1 = 128, l2 = 128;
    int t0 = 128, t1 = 128, t2 = 128;
    for (var y = 0; y < height; ++y) {
      var row = y * stride;
      var above = row - stride;
      for (var x = 0; x < stride; x += 4) {
        int g0 = 0, g2 = 0, g1 = 0, g3 = 0;
        if (y > 0) {
          g0 = picture[above + x] - t0;
          g2 = picture[above + x + 2] - t0;
          g1 = picture[above + x + 1] - t1;
          g3 = picture[above + x + 3] - t2;
        }

        residues[row + x] = (byte)(picture[row + x] - l0 - g0);
        residues[row + x + 2] = (byte)(picture[row + x + 2] - l0 - g2);
        l0 = picture[row + x + 2];
        residues[row + x + 1] = (byte)(picture[row + x + 1] - l1 - g1);
        l1 = picture[row + x + 1];
        residues[row + x + 3] = (byte)(picture[row + x + 3] - l2 - g3);
        l2 = picture[row + x + 3];
        if (y > 0) {
          t0 = picture[above + x + 2];
          t1 = picture[above + x + 1];
          t2 = picture[above + x + 3];
        }
      }
    }
    return residues;
  }
}
