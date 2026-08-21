using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.FlashSv2;

/// <summary>
/// DEFLATE (RFC 1951), decoded from the standard's own text rather than from any implementation,
/// because it is the one piece Screen Video v2's "priming" needs that no wrapper this package already
/// uses can be asked for: a preset dictionary seeded into the sliding window before the first bit is
/// read. Neither .NET's <see cref="System.IO.Compression.DeflateStream"/> nor its
/// <see cref="System.IO.Compression.ZLibStream"/> exposes one.
/// </summary>
/// <remarks>
/// Verified independently of anything this decodes for Screen Video v2: run with an empty dictionary
/// over the raw deflate payload inside an ordinary zlib stream — the header and the four-byte Adler-32
/// trailer stripped off — its output was checked byte for byte against <c>ZLibStream</c>'s own decode of
/// the same bytes, which owes this implementation nothing and could not be made to agree by sharing a
/// mistake with it. Only once that agreed was this trusted for the case <c>ZLibStream</c> cannot do at
/// all.
/// <para/>
/// Two things about the bit order are easy to get backwards and both matter: everything that is not a
/// Huffman code — the block header, a stored block's length, a repeat count's extra bits — is packed
/// least-significant-bit first, precisely as every other bit-packed field in this package already reads;
/// a Huffman code itself is packed most-significant-bit first, which RFC 1951 section 3.1.1 states
/// outright and which is the one place this format's bit order reverses inside a single byte.
/// </remarks>
internal static class RawDeflate {

  private static readonly int[] _LengthBase = [
    3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258,
  ];

  private static readonly int[] _LengthExtraBits = [
    0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0,
  ];

  private static readonly int[] _DistanceBase = [
    1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769,
    1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577,
  ];

  private static readonly int[] _DistanceExtraBits = [
    0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13,
  ];

  private static readonly int[] _CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

  private const int _MaxDictionary = 32768;

  /// <summary>A canonical Huffman decode table, built from RFC 1951 section 3.2.2's own procedure:
  /// count the codes at each length, assign the smallest code at each length in order, then hand out
  /// consecutive values to the symbols that length holds in symbol order.</summary>
  private sealed class _HuffmanTable {
    private readonly Dictionary<(int Length, int Code), int> _symbols = [];

    public _HuffmanTable(ReadOnlySpan<int> lengths) {
      var maxLength = 0;
      foreach (var length in lengths)
        if (length > maxLength)
          maxLength = length;

      if (maxLength == 0)
        return;

      var blCount = new int[maxLength + 1];
      foreach (var length in lengths)
        if (length > 0)
          ++blCount[length];

      var nextCode = new int[maxLength + 1];
      var code = 0;
      for (var bits = 1; bits <= maxLength; ++bits) {
        code = (code + blCount[bits - 1]) << 1;
        nextCode[bits] = code;
      }

      for (var symbol = 0; symbol < lengths.Length; ++symbol) {
        var length = lengths[symbol];
        if (length == 0)
          continue;

        this._symbols[(length, nextCode[length])] = symbol;
        ++nextCode[length];
      }
    }

    /// <summary>Reads one Huffman code, most-significant-bit first, and returns the symbol it names.</summary>
    public int Decode(ref _BitReader reader) {
      var code = 0;
      for (var length = 1; length <= 15; ++length) {
        code = (code << 1) | reader.ReadBit();
        if (this._symbols.TryGetValue((length, code), out var symbol))
          return symbol;
      }

      throw new InvalidDataException("A DEFLATE Huffman code did not match any code this table assigns within fifteen bits.");
    }
  }

  private ref struct _BitReader(ReadOnlySpan<byte> data) {
    private readonly ReadOnlySpan<byte> _data = data;
    private int _bytePosition;
    private int _bitPosition;

    /// <summary>The next bit, least-significant first within its byte — the packing every field here
    /// uses except a Huffman code itself.</summary>
    public int ReadBit() {
      if (this._bytePosition >= this._data.Length)
        throw new InvalidDataException("A DEFLATE stream ran out of bits before its data was fully read.");

      var bit = (this._data[this._bytePosition] >> this._bitPosition) & 1;
      if (++this._bitPosition == 8) {
        this._bitPosition = 0;
        ++this._bytePosition;
      }

      return bit;
    }

    public int ReadBits(int count) {
      var value = 0;
      for (var i = 0; i < count; ++i)
        value |= this.ReadBit() << i;

      return value;
    }

    public void AlignToByte() {
      if (this._bitPosition != 0) {
        this._bitPosition = 0;
        ++this._bytePosition;
      }
    }

    public byte ReadByte() {
      this.AlignToByte();
      if (this._bytePosition >= this._data.Length)
        throw new InvalidDataException("A DEFLATE stored block ran out of bytes before its stated length was read.");

      return this._data[this._bytePosition++];
    }
  }

  private static readonly int[] _FixedLiteralLengths = _BuildFixedLiteralLengths();
  private static readonly int[] _FixedDistanceLengths = _BuildFixedDistanceLengths();

  private static int[] _BuildFixedLiteralLengths() {
    var lengths = new int[288];
    for (var i = 0; i <= 143; ++i) lengths[i] = 8;
    for (var i = 144; i <= 255; ++i) lengths[i] = 9;
    for (var i = 256; i <= 279; ++i) lengths[i] = 7;
    for (var i = 280; i <= 287; ++i) lengths[i] = 8;
    return lengths;
  }

  private static int[] _BuildFixedDistanceLengths() {
    var lengths = new int[30];
    for (var i = 0; i < 30; ++i) lengths[i] = 5;
    return lengths;
  }

  /// <summary>
  /// Decodes a raw DEFLATE stream (no zlib or gzip wrapper), with the sliding window preloaded from
  /// <paramref name="dictionary"/> as if those bytes had just been produced — so a back-reference may
  /// point into them — and returns only the bytes this stream itself produced.
  /// </summary>
  public static byte[] Decode(ReadOnlySpan<byte> compressed, ReadOnlySpan<byte> dictionary) {
    var seed = dictionary.Length > _MaxDictionary ? dictionary[^_MaxDictionary..] : dictionary;
    var output = new List<byte>(seed.Length + compressed.Length * 3);
    output.AddRange(seed.ToArray());
    var outputStart = output.Count;

    var reader = new _BitReader(compressed);
    while (true) {
      var final = reader.ReadBit();
      var type = reader.ReadBits(2);

      switch (type) {
        case 0: _DecodeStored(ref reader, output); break;
        case 1: _DecodeHuffman(ref reader, output, new(_FixedLiteralLengths), new(_FixedDistanceLengths)); break;
        case 2: _DecodeDynamic(ref reader, output); break;
        default: throw new InvalidDataException("A DEFLATE block header names block type 3, which the standard reserves and does not define.");
      }

      if (final != 0)
        break;
    }

    return output.GetRange(outputStart, output.Count - outputStart).ToArray();
  }

  private static void _DecodeStored(ref _BitReader reader, List<byte> output) {
    reader.AlignToByte();
    var lengthLow = reader.ReadByte();
    var lengthHigh = reader.ReadByte();
    var length = lengthLow | (lengthHigh << 8);
    var complementLow = reader.ReadByte();
    var complementHigh = reader.ReadByte();
    var complement = complementLow | (complementHigh << 8);

    if ((length ^ complement) != 0xFFFF)
      throw new InvalidDataException("A DEFLATE stored block's length and its one's complement do not agree.");

    for (var i = 0; i < length; ++i)
      output.Add(reader.ReadByte());
  }

  private static void _DecodeDynamic(ref _BitReader reader, List<byte> output) {
    var literalCount = reader.ReadBits(5) + 257;
    var distanceCount = reader.ReadBits(5) + 1;
    var codeLengthCount = reader.ReadBits(4) + 4;

    var codeLengthLengths = new int[19];
    for (var i = 0; i < codeLengthCount; ++i)
      codeLengthLengths[_CodeLengthOrder[i]] = reader.ReadBits(3);

    var codeLengthTable = new _HuffmanTable(codeLengthLengths);

    var lengths = new int[literalCount + distanceCount];
    var position = 0;
    while (position < lengths.Length) {
      var symbol = codeLengthTable.Decode(ref reader);
      if (symbol < 16) {
        lengths[position++] = symbol;
      } else if (symbol == 16) {
        if (position == 0)
          throw new InvalidDataException("A DEFLATE dynamic Huffman table repeats a previous code length before any length was read.");

        var repeat = reader.ReadBits(2) + 3;
        var previous = lengths[position - 1];
        for (var i = 0; i < repeat; ++i)
          lengths[position++] = previous;
      } else if (symbol == 17) {
        var repeat = reader.ReadBits(3) + 3;
        for (var i = 0; i < repeat; ++i)
          lengths[position++] = 0;
      } else {
        var repeat = reader.ReadBits(7) + 11;
        for (var i = 0; i < repeat; ++i)
          lengths[position++] = 0;
      }
    }

    var literalLengths = lengths.AsSpan(0, literalCount);
    var distanceLengths = lengths.AsSpan(literalCount, distanceCount);
    _DecodeHuffman(ref reader, output, new(literalLengths), new(distanceLengths));
  }

  private static void _DecodeHuffman(ref _BitReader reader, List<byte> output, _HuffmanTable literals, _HuffmanTable distances) {
    while (true) {
      var symbol = literals.Decode(ref reader);
      if (symbol < 256) {
        output.Add((byte)symbol);
        continue;
      }

      if (symbol == 256)
        return;

      var lengthIndex = symbol - 257;
      if (lengthIndex >= _LengthBase.Length)
        throw new InvalidDataException($"A DEFLATE stream's literal/length alphabet named symbol {symbol}, which no length code uses.");

      var length = _LengthBase[lengthIndex] + reader.ReadBits(_LengthExtraBits[lengthIndex]);
      var distanceSymbol = distances.Decode(ref reader);
      if (distanceSymbol >= _DistanceBase.Length)
        throw new InvalidDataException($"A DEFLATE stream's distance alphabet named symbol {distanceSymbol}, which no distance code uses.");

      var distance = _DistanceBase[distanceSymbol] + reader.ReadBits(_DistanceExtraBits[distanceSymbol]);
      if (distance > output.Count)
        throw new InvalidDataException(
          $"A DEFLATE stream's match reaches {distance} byte(s) back from a position only {output.Count} byte(s) "
          + "into the output and its preset dictionary combined.");

      var start = output.Count - distance;
      for (var i = 0; i < length; ++i)
        output.Add(output[start + i]);
    }
  }
}
