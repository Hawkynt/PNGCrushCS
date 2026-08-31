using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes the MidiVid Archive Codec (<c>MVHA</c>).</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/mvha.c</c>, copyright (c) 2019 Paul B Mahol, and the
/// predictor primitives in <c>libavcodec/lossless_videodsp.c</c>; both are distributed there under
/// LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// MVHA has two packet payloads. One is zlib-compressed residual planes. The other carries symbol
/// probabilities, rebuilds a Huffman tree from those counts, and entropy-decodes the same residual
/// planes. The planes are stored bottom-up as YUV 4:2:2, then restored with a left predictor on the
/// first coded row and the lossless median predictor on subsequent rows.
/// </remarks>
public sealed class MvhaVideoDecoder : IVideoCodecDecoder<MvhaVideoDecoder> {

  private const uint _LZYV = (uint)'L' | ((uint)'Z' << 8) | ((uint)'Y' << 16) | ((uint)'V' << 24);
  private const uint _HUFY = (uint)'H' | ((uint)'U' << 8) | ((uint)'F' << 16) | ((uint)'Y' << 24);
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MVHA");

  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _planeBytes;
  private readonly int _streamIndex;

  private MvhaVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._chromaWidth = width / 2;
    this._planeBytes = checked(width * height + 2 * this._chromaWidth * height);
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "MidiVid Archive Codec";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static MvhaVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"MVHA stream {stream.Index} states an invalid {stream.Width}x{stream.Height} picture.");
    if ((stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"MVHA stream {stream.Index} has odd-width 4:2:2 chroma ({stream.Width} pixels).");
    if ((long)stream.Width * stream.Height * 2 > int.MaxValue)
      throw new InvalidDataException($"MVHA stream {stream.Index} is too large to hold in one managed frame.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    if (source.Length <= 8)
      throw new InvalidDataException($"MVHA stream {this._streamIndex} supplied a packet shorter than its eight-byte header.");

    var type = BinaryPrimitives.ReadUInt32BigEndian(source);
    var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
    if (declaredSize < 1 || declaredSize >= source.Length)
      throw new InvalidDataException(
        $"An MVHA packet states payload size {declaredSize} for a {source.Length}-byte packet.");

    var residuals = type switch {
      _LZYV => this._InflateResiduals(source[8..]),
      _HUFY => this._DecodeHuffmanResiduals(source[8..]),
      _ => throw new NotSupportedException($"MVHA packet type 0x{type:X8} is not defined."),
    };

    var yLength = checked(this._width * this._height);
    var chromaLength = checked(this._chromaWidth * this._height);
    var y = residuals.AsSpan(0, yLength);
    var u = residuals.AsSpan(yLength, chromaLength);
    var v = residuals.AsSpan(yLength + chromaLength, chromaLength);
    _RestorePlane(y, this._width, this._height);
    _RestorePlane(u, this._chromaWidth, this._height);
    _RestorePlane(v, this._chromaWidth, this._height);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(y, u, v),
    };
    return true;
  }

  private byte[] _InflateResiduals(ReadOnlySpan<byte> compressed) {
    var residuals = new byte[this._planeBytes];
    using var input = new MemoryStream(compressed.ToArray(), writable: false);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);

    var at = 0;
    while (at < residuals.Length) {
      var read = zlib.Read(residuals, at, residuals.Length - at);
      if (read == 0)
        break;
      at += read;
    }

    // FFmpeg zero-fills any remainder when the zlib stream terminates before a requested row.
    return residuals;
  }

  private byte[] _DecodeHuffmanResiduals(ReadOnlySpan<byte> payload) {
    var bits = new MsbBitReader(payload);
    bits.Skip(24);
    var firstSymbol = checked((int)bits.ReadBits(8));
    var symbolCount = checked((int)bits.ReadBits(8)) + 1;
    var symbols = new byte[symbolCount];
    var probabilities = new uint[symbolCount];

    var symbol = firstSymbol;
    for (var i = 0; i < symbolCount; ++symbol) {
      var probability = bits.ReadBits(1) != 0 ? bits.ReadBits(12) : bits.ReadBits(3);
      if (probability == 0)
        continue;
      symbols[i] = unchecked((byte)symbol);
      probabilities[i] = probability;
      ++i;
    }

    var tree = HuffmanTree.Build(symbols, probabilities);
    var residuals = new byte[this._planeBytes];
    for (var i = 0; i < residuals.Length; ++i)
      residuals[i] = tree.Read(ref bits);
    return residuals;
  }

  private static void _RestorePlane(Span<byte> plane, int width, int height) {
    if (width == 0 || height == 0)
      return;

    var accumulator = 0;
    for (var x = 0; x < width; ++x) {
      accumulator += plane[x];
      plane[x] = unchecked((byte)accumulator);
    }

    for (var row = 1; row < height; ++row) {
      var rowAt = row * width;
      var aboveAt = rowAt - width;
      plane[rowAt] = unchecked((byte)(plane[aboveAt] + plane[rowAt]));
      for (var x = 1; x < width; ++x) {
        var left = plane[rowAt + x - 1];
        var above = plane[aboveAt + x];
        var aboveLeft = plane[aboveAt + x - 1];
        var gradient = (left + above - aboveLeft) & 0xFF;
        var prediction = _Median(left, above, gradient);
        plane[rowAt + x] = unchecked((byte)(prediction + plane[rowAt + x]));
      }
    }
  }

  private byte[] _ToRgb24(ReadOnlySpan<byte> y, ReadOnlySpan<byte> u, ReadOnlySpan<byte> v) {
    var output = new byte[checked(this._width * this._height * 3)];
    var at = 0;
    for (var displayRow = 0; displayRow < this._height; ++displayRow) {
      var codedRow = this._height - 1 - displayRow;
      for (var column = 0; column < this._width; ++column) {
        var yy = y[codedRow * this._width + column];
        var chromaAt = codedRow * this._chromaWidth + (column >> 1);
        var cb = u[chromaAt];
        var cr = v[chromaAt];
        var c = yy - 16;
        var d = cb - 128;
        var e = cr - 128;
        output[at++] = _Clamp((298 * c + 409 * e + 128) >> 8);
        output[at++] = _Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
        output[at++] = _Clamp((298 * c + 516 * d + 128) >> 8);
      }
    }
    return output;
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);
    if (c < a)
      return a;
    if (c > b)
      return b;
    return c;
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private sealed class HuffmanTree {
    private readonly Node[] _nodes;
    private readonly int _root;
    private readonly bool _single;

    private HuffmanTree(Node[] nodes, int root, bool single) {
      this._nodes = nodes;
      this._root = root;
      this._single = single;
    }

    internal static HuffmanTree Build(ReadOnlySpan<byte> symbols, ReadOnlySpan<uint> probabilities) {
      if (symbols.Length == 0 || symbols.Length != probabilities.Length)
        throw new InvalidDataException("An MVHA Huffman description contains no usable symbols.");

      var nodes = new Node[checked(symbols.Length * 2)];
      for (var i = 0; i < symbols.Length; ++i) {
        if (probabilities[i] == 0)
          throw new InvalidDataException("An MVHA Huffman leaf has zero probability.");
        nodes[i] = new(probabilities[i], symbols[i], i, i);
      }

      if (symbols.Length == 1)
        return new(nodes, 0, single: true);

      var current = symbols.Length;
      while (true) {
        if (current >= nodes.Length)
          throw new InvalidDataException("An MVHA Huffman tree requires too many nodes.");

        nodes[current] = new(uint.MaxValue, -1, -1, -1);
        var first = current;
        var second = current;
        for (var candidate = 0; candidate < current; ++candidate) {
          var value = nodes[candidate].Count;
          if (value == 0 || value >= nodes[first].Count)
            continue;

          if (value >= nodes[second].Count) {
            first = candidate;
          } else {
            first = second;
            second = candidate;
          }
        }

        if (first == current)
          break;

        var a = nodes[second].Count;
        var b = nodes[first].Count;
        if (a >= uint.MaxValue - b)
          throw new InvalidDataException("An MVHA Huffman probability sum overflows 32 bits.");
        nodes[second].Count = 0;
        nodes[first].Count = 0;
        nodes[current] = new(a + b, -1, first, second);
        ++current;
      }

      return new(nodes, current - 1, single: false);
    }

    internal byte Read(ref MsbBitReader bits) {
      if (this._single) {
        // FFmpeg gives a one-symbol tree an explicit one-bit code of 1 and adds one to the byte
        // symbol (with byte wrap) when building the translation table.
        if (bits.ReadBits(1) != 1)
          throw new InvalidDataException("An MVHA one-symbol Huffman code is not its defined 1 bit.");
        return unchecked((byte)(this._nodes[this._root].Symbol + 1));
      }

      var node = this._root;
      while (this._nodes[node].Symbol < 0) {
        // get_tree_codes() complements the branch prefix before handing it to FFmpeg's VLC builder:
        // coded 1 therefore selects the tree's left branch and coded 0 the right branch.
        node = bits.ReadBits(1) != 0 ? this._nodes[node].Left : this._nodes[node].Right;
        if ((uint)node >= this._nodes.Length)
          throw new InvalidDataException("An MVHA Huffman code walks outside its reconstructed tree.");
      }
      return checked((byte)this._nodes[node].Symbol);
    }
  }

  private struct Node {
    internal uint Count;
    internal readonly int Symbol;
    internal readonly int Left;
    internal readonly int Right;

    internal Node(uint count, int symbol, int left, int right) {
      this.Count = count;
      this.Symbol = symbol;
      this.Left = left;
      this.Right = right;
    }
  }

  private ref struct MsbBitReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    internal MsbBitReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._position = 0;
    }

    internal void Skip(int count) {
      if (count < 0 || this._data.Length * 8 - this._position < count)
        throw new InvalidDataException("An MVHA bitstream ends inside its header.");
      this._position += count;
    }

    internal uint ReadBits(int count) {
      if (count < 0 || count > 32 || this._data.Length * 8 - this._position < count)
        throw new InvalidDataException($"An MVHA bitstream ends while reading {count} bit(s).");

      uint value = 0;
      for (var i = 0; i < count; ++i) {
        var absolute = this._position++;
        value = (value << 1) | (uint)((this._data[absolute >> 3] >> (7 - (absolute & 7))) & 1);
      }
      return value;
    }
  }
}
