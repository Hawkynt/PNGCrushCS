using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>The fixed Huffman code books used by headerless/version-1 HuffYUV.</summary>
/// <remarks>
/// These are interoperability constants from the original codec, as preserved by FFmpeg. Unlike
/// later HuffYUV descriptions the old stream carries neither lengths nor codes, so a decoder cannot
/// derive them from the file. The compact <c>shift</c> arrays are the same run-length representation
/// later descriptions use; the <c>add</c> arrays are the corresponding right-justified code words.
/// </remarks>
internal sealed class HuffYuvLegacyTable {

  private sealed class _Node {
    internal int Zero = -1;
    internal int One = -1;
    internal int Symbol = -1;
  }

  private readonly byte[] _lengths;
  private readonly byte[] _codes;
  private readonly List<_Node> _nodes = [new()];

  private HuffYuvLegacyTable(ReadOnlySpan<byte> shift, ReadOnlySpan<byte> codes) {
    this._lengths = new byte[HuffYuvHuffmanTable.SYMBOL_COUNT];
    HuffYuvHuffmanTable.ReadLengths(shift, 0, this._lengths, 0);
    this._codes = codes.ToArray();
    if (this._codes.Length != this._lengths.Length)
      throw new InvalidDataException("The classic HuffYUV code book is incomplete.");

    for (var symbol = 0; symbol < this._lengths.Length; ++symbol) {
      var length = this._lengths[symbol];
      var node = 0;
      for (var bitIndex = length - 1; bitIndex >= 0; --bitIndex) {
        var one = ((this._codes[symbol] >> bitIndex) & 1) != 0;
        var next = one ? this._nodes[node].One : this._nodes[node].Zero;
        if (next < 0) {
          next = this._nodes.Count;
          this._nodes.Add(new());
          if (one)
            this._nodes[node].One = next;
          else
            this._nodes[node].Zero = next;
        }
        node = next;
      }
      if (this._nodes[node].Symbol >= 0)
        throw new InvalidDataException("The classic HuffYUV code book assigns two symbols to one code.");
      this._nodes[node].Symbol = symbol;
    }
  }

  internal int Read(HuffYuvBitReader bits) {
    var node = 0;
    for (var depth = 0; depth < 32; ++depth) {
      node = bits.Bit() == 0 ? this._nodes[node].Zero : this._nodes[node].One;
      if (node < 0)
        throw new InvalidDataException("The frame contains a code absent from the classic HuffYUV table.");
      var symbol = this._nodes[node].Symbol;
      if (symbol >= 0)
        return symbol;
    }
    throw new InvalidDataException("A classic HuffYUV code exceeds 32 bits.");
  }

  internal void Write(HuffYuvBitWriter bits, int symbol) => bits.Write(this._codes[symbol], this._lengths[symbol]);

  internal static HuffYuvLegacyTable[] ForBitstreamDepth(int bitsPerPixel) {
    var luma = new HuffYuvLegacyTable(_SHIFT_LUMA, _ADD_LUMA);
    var chroma = new HuffYuvLegacyTable(_SHIFT_CHROMA, _ADD_CHROMA);
    return bitsPerPixel >= 24 ? [luma, luma, luma] : [luma, chroma, chroma];
  }

  private static ReadOnlySpan<byte> _SHIFT_LUMA => [
    34,36,35,69,135,232,9,16,10,24,11,23,12,16,13,10,
    14,8,15,8,16,8,17,20,16,10,207,206,205,236,11,8,
    10,21,9,23,8,8,199,70,69,68,
  ];

  private static ReadOnlySpan<byte> _SHIFT_CHROMA => [
    66,36,37,38,39,40,41,75,76,77,110,239,144,81,82,83,
    84,85,118,183,56,57,88,89,56,89,154,57,58,57,26,141,
    57,56,58,57,58,57,184,119,214,245,116,83,82,49,80,79,
    78,77,44,75,41,40,39,38,37,36,34,
  ];

  private static ReadOnlySpan<byte> _ADD_LUMA => [
    3,9,5,12,10,35,32,29,27,50,48,45,44,41,39,37,
    73,70,68,65,64,61,58,56,53,50,49,46,44,41,38,36,
    68,65,63,61,58,55,53,51,48,46,45,43,41,39,38,36,
    35,33,32,30,29,27,26,25,48,47,46,44,43,41,40,39,
    37,36,35,34,32,31,30,28,27,26,24,23,22,20,19,37,
    35,34,33,31,30,29,27,26,24,23,21,20,18,17,15,29,
    27,26,24,22,21,19,17,16,14,26,25,23,21,19,18,16,
    15,27,25,23,21,19,17,16,14,26,25,23,21,18,17,14,
    12,17,19,13,4,9,2,11,1,7,8,0,16,3,14,6,
    12,10,5,15,18,11,10,13,15,16,19,20,22,24,27,15,
    18,20,22,24,26,14,17,20,22,24,27,15,18,20,23,25,
    28,16,19,22,25,28,32,36,21,25,29,33,38,42,45,49,
    28,31,34,37,40,42,44,47,49,50,52,54,56,57,59,60,
    62,64,66,67,69,35,37,39,40,42,43,45,47,48,51,52,
    54,55,57,59,60,62,63,66,67,69,71,72,38,40,42,43,
    46,47,49,51,26,28,30,31,33,34,18,19,11,13,7,8,
  ];

  private static ReadOnlySpan<byte> _ADD_CHROMA => [
    3,1,2,2,2,2,3,3,7,5,7,5,8,6,11,9,
    7,13,11,10,9,8,7,5,9,7,6,4,7,5,8,7,
    11,8,13,11,19,15,22,23,20,33,32,28,27,29,51,77,
    43,45,76,81,46,82,75,55,56,144,58,80,60,74,147,63,
    143,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,
    80,81,82,83,84,85,86,87,88,89,90,91,27,30,21,22,
    17,14,5,6,100,54,47,50,51,53,106,107,108,109,110,111,
    112,113,114,115,4,117,118,92,94,121,122,3,124,103,2,1,
    0,129,130,131,120,119,126,125,136,137,138,139,140,141,142,134,
    135,132,133,104,64,101,62,57,102,95,93,59,61,28,97,96,
    52,49,48,29,32,25,24,46,23,98,45,44,43,20,42,41,
    19,18,99,40,15,39,38,16,13,12,11,37,10,9,8,36,
    7,128,127,105,123,116,35,34,33,145,31,79,42,146,78,26,
    83,48,49,50,44,47,26,31,30,18,17,19,21,24,25,13,
    14,16,17,18,20,21,12,14,15,9,10,6,9,6,5,8,
    6,12,8,10,7,9,6,4,6,2,2,3,3,3,3,2,
  ];
}
