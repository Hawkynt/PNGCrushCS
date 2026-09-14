using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Duck/On2 TrueMotion 2 (<c>TM20</c>): seven independently Huffman-coded token streams drive
/// 4x4 luminance / 2x2 chroma blocks, with still, additive-update and motion blocks predicted from
/// the immediately previous picture.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/truemotion2.c</c>, copyright (c) 2005 Konstantin Shishkov,
/// LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// TrueMotion 2 has no bidirectional or future-reference picture type. A picture is a key frame when
/// every block is spatially reconstructed (types 0-3); the update, still and motion block types
/// (4-6) all reference only the picture immediately before it, making the picture a P frame.
/// <para/>
/// The stream is self-describing: each token stream may carry a signed delta table and carries its
/// own Huffman tree. The tree bits are stored most-significant-bit first inside little-endian 32-bit
/// words. The leaves' encounter order matters: FFmpeg's <c>ff_vlc_init_from_lengths</c> assigns codes
/// in exactly that order rather than sorting symbols by length, and this decoder reproduces that rule.
/// <para/>
/// Both header magics seen by the reference decoder are accepted. Geometry must be divisible by four,
/// because every block covers 4x4 luminance samples and 2x2 chroma samples. Malformed stream sizes,
/// trees, token indices, token underruns and motion vectors are rejected rather than clipped into a
/// plausible but wrong picture.
/// </remarks>
public sealed class TrueMotion2VideoDecoder : IVideoCodecDecoder<TrueMotion2VideoDecoder> {

  private enum TokenStream { ChromaHigh, ChromaLow, LumaHigh, LumaLow, Update, Motion, Type }
  private enum BlockType { HighResolution, MediumResolution, LowResolution, NullResolution, Update, Still, Motion }

  private const int _HEADER_SIZE = 40;
  private const int _DELTA_COUNT = 64;
  private const uint _ESCAPE = 0x80000000;
  private const uint _OLD_MAGIC = 0x00000100;
  private const uint _NEW_MAGIC = 0x00000101;
  private const int _STREAM_COUNT = 7;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("TM20");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _chromaWidth;
  private readonly int[][] _deltas = _CreateDeltaTables();
  private readonly int[] _last;
  private readonly int[] _chromaLast;
  private readonly int[] _verticalDelta = new int[4];
  private readonly int[] _chromaDelta = new int[4];
  private readonly FramePlanes[] _frames;
  private int _current;
  private bool _hasReference;

  private TrueMotion2VideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._chromaWidth = width >> 1;
    this._last = new int[width];
    this._chromaLast = new int[width];
    this._frames = [new(width, height), new(width, height)];
  }

  public static string CodecName => "Duck TrueMotion 2";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static TrueMotion2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"TrueMotion 2 stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which has no pixels.");
    if ((stream.Width & 3) != 0 || (stream.Height & 3) != 0)
      throw new NotSupportedException(
        $"TrueMotion 2 codes 4x4 blocks and requires both dimensions to be multiples of four; stream {stream.Index} "
        + $"states {stream.Width}x{stream.Height}.");
    if ((long)stream.Width * stream.Height > int.MaxValue)
      throw new NotSupportedException(
        $"TrueMotion 2 stream {stream.Index} is {stream.Width}x{stream.Height}, too large for an in-memory frame.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _HEADER_SIZE)
      throw this._Invalid($"packet is {data.Length} bytes, shorter than the {_HEADER_SIZE}-byte frame header");
    if ((data.Length & 3) != 0)
      throw this._Invalid($"packet is {data.Length} bytes, not a whole number of 32-bit words");

    var magic = BinaryPrimitives.ReadUInt32BigEndian(data);
    if (magic is not (_OLD_MAGIC or _NEW_MAGIC))
      throw this._Invalid($"frame header magic is 0x{magic:X8}, not 0x{_OLD_MAGIC:X8} or 0x{_NEW_MAGIC:X8}");

    var streams = new TokenReader[_STREAM_COUNT];
    var offset = _HEADER_SIZE;
    for (var i = 0; i < _STREAM_COUNT; ++i) {
      var parsed = this._ReadStream(data[offset..], (TokenStream)i);
      streams[i] = new(parsed.Tokens);
      offset = checked(offset + parsed.BytesConsumed);
      if (offset > data.Length)
        throw this._Invalid($"token stream {i} ends beyond the packet");
    }

    var current = this._frames[this._current];
    var previous = this._frames[this._current ^ 1];
    Array.Clear(this._last);
    Array.Clear(this._chromaLast);
    var blocksWide = this._width >> 2;
    var blocksHigh = this._height >> 2;

    if (streams[(int)TokenStream.Type].Remaining < checked(blocksWide * blocksHigh))
      throw this._Invalid(
        $"type stream carries {streams[(int)TokenStream.Type].Remaining} token(s) for {blocksWide * blocksHigh} blocks");

    for (var blockY = 0; blockY < blocksHigh; ++blockY) {
      Array.Clear(this._verticalDelta);
      Array.Clear(this._chromaDelta);
      for (var blockX = 0; blockX < blocksWide; ++blockX) {
        var typeValue = streams[(int)TokenStream.Type].NextRaw(this._streamIndex, TokenStream.Type);
        if ((uint)typeValue > (uint)BlockType.Motion)
          throw this._Invalid($"block {blockX},{blockY} has unknown type {typeValue}");

        var type = (BlockType)typeValue;
        switch (type) {
          case BlockType.HighResolution:
            this._HighResolution(current, blockX, blockY, streams);
            break;
          case BlockType.MediumResolution:
            this._MediumResolution(current, blockX, blockY, streams);
            break;
          case BlockType.LowResolution:
            this._LowResolution(current, blockX, blockY, streams);
            break;
          case BlockType.NullResolution:
            this._NullResolution(current, blockX, blockY);
            break;
          case BlockType.Update:
            this._RequireReference(type, blockX, blockY);
            this._Update(current, previous, blockX, blockY, streams);
            break;
          case BlockType.Still:
            this._RequireReference(type, blockX, blockY);
            this._Still(current, previous, blockX, blockY);
            break;
          case BlockType.Motion:
            this._RequireReference(type, blockX, blockY);
            this._Motion(current, previous, blockX, blockY, streams);
            break;
          default:
            throw new InvalidOperationException();
        }
      }
    }

    frame = this._ToRawImage(current);
    this._current ^= 1;
    this._hasReference = true;
    return true;
  }

  private void _RequireReference(BlockType type, int blockX, int blockY) {
    if (!this._hasReference)
      throw this._Invalid($"first picture uses {type} block {blockX},{blockY}, which requires a previous picture");
  }

  private ParsedStream _ReadStream(ReadOnlySpan<byte> data, TokenStream stream) {
    if (data.Length < 4)
      throw this._Invalid($"token stream {(int)stream} has no length word");

    var words = BinaryPrimitives.ReadUInt32LittleEndian(data);
    if (words == 0)
      return new(4, []);
    if (words > (uint)((data.Length - 4) / 4))
      throw this._Invalid($"token stream {(int)stream} states {words} payload words but only {(data.Length - 4) / 4} remain");

    var totalBytes = checked((int)words * 4 + 4);
    var body = data[..totalBytes];
    var position = 4;
    var tokenField = _ReadUInt32(body, ref position, this._streamIndex, $"token stream {(int)stream} token count");
    var tokenCount = tokenField >> 1;
    if (tokenCount > 0xFFFFFF)
      throw this._Invalid($"token stream {(int)stream} states {tokenCount} tokens, over the format's 24-bit sanity bound");

    if ((tokenField & 1) != 0) {
      var deltaMarker = _ReadUInt32(body, ref position, this._streamIndex, $"token stream {(int)stream} delta marker");
      if (deltaMarker == _ESCAPE)
        deltaMarker = _ReadUInt32(body, ref position, this._streamIndex, $"token stream {(int)stream} escaped delta marker");
      if (deltaMarker != 0) {
        var bits = new WordBitReader(body[position..]);
        this._ReadDeltaTable(ref bits, stream);
        position = checked(position + bits.AlignedBytesConsumed);
      }
    }

    var treeMarker = _ReadUInt32(body, ref position, this._streamIndex, $"token stream {(int)stream} tree marker");
    if (treeMarker == _ESCAPE)
      _Skip(body, ref position, 8, this._streamIndex, $"token stream {(int)stream} escaped tree metadata");
    else
      _Skip(body, ref position, 4, this._streamIndex, $"token stream {(int)stream} tree metadata");

    var treeBits = new WordBitReader(body[position..]);
    var huffman = this._ReadHuffmanTree(ref treeBits, stream);
    position = checked(position + treeBits.AlignedBytesConsumed);

    var tokenMarker = _ReadUInt32(body, ref position, this._streamIndex, $"token stream {(int)stream} token-data marker");
    var tokens = new int[checked((int)tokenCount)];
    if (tokenMarker != 0) {
      var tokenBits = new WordBitReader(body[position..]);
      for (var i = 0; i < tokens.Length; ++i) {
        tokens[i] = huffman.Decode(ref tokenBits, this._streamIndex, stream);
        this._ValidateToken(stream, tokens[i], i);
      }
    } else {
      Array.Fill(tokens, huffman.FirstSymbol);
      for (var i = 0; i < tokens.Length; ++i)
        this._ValidateToken(stream, tokens[i], i);
    }

    return new(totalBytes, tokens);
  }

  private void _ReadDeltaTable(ref WordBitReader bits, TokenStream stream) {
    var count = checked((int)bits.Read(9, this._streamIndex, $"token stream {(int)stream} delta count"));
    var width = checked((int)bits.Read(5, this._streamIndex, $"token stream {(int)stream} delta width"));
    if (count is < 1 or > _DELTA_COUNT || width is < 1 or > 31)
      throw this._Invalid($"token stream {(int)stream} has an invalid delta table of {count} value(s) at {width} bit(s) each");

    var table = this._deltas[(int)stream];
    for (var i = 0; i < count; ++i) {
      var value = bits.Read(width, this._streamIndex, $"token stream {(int)stream} delta {i}");
      var sign = 1u << (width - 1);
      table[i] = (value & sign) == 0 ? checked((int)value) : unchecked((int)(value - (1u << width)));
    }
    Array.Clear(table, count, table.Length - count);
  }

  private HuffmanDecoder _ReadHuffmanTree(ref WordBitReader bits, TokenStream stream) {
    var valueBits = checked((int)bits.Read(5, this._streamIndex, $"token stream {(int)stream} literal width"));
    var maximumBits = checked((int)bits.Read(5, this._streamIndex, $"token stream {(int)stream} maximum code length"));
    _ = bits.Read(5, this._streamIndex, $"token stream {(int)stream} minimum code length");
    var nodeCount = checked((int)bits.Read(17, this._streamIndex, $"token stream {(int)stream} tree node count"));

    if (valueBits is < 1 or > 32 || maximumBits is < 0 or > 25)
      throw this._Invalid($"token stream {(int)stream} has invalid Huffman widths {valueBits}/{maximumBits}");
    if (nodeCount is <= 0 or > 0x10000)
      throw this._Invalid($"token stream {(int)stream} has invalid Huffman node count {nodeCount}");

    if (maximumBits == 0)
      maximumBits = 1;
    var expectedLeaves = (nodeCount + 1) >> 1;
    var leaves = new List<HuffmanLeaf>(expectedLeaves);
    var deepest = this._ReadTreeNode(ref bits, stream, valueBits, maximumBits, 0, leaves, expectedLeaves);
    if (deepest != maximumBits)
      throw this._Invalid($"token stream {(int)stream} Huffman tree reaches depth {deepest}, not its stated {maximumBits}");
    if (leaves.Count != expectedLeaves)
      throw this._Invalid($"token stream {(int)stream} Huffman tree has {leaves.Count} leaves, not {expectedLeaves}");

    return HuffmanDecoder.Build(leaves, this._streamIndex, stream);
  }

  private int _ReadTreeNode(
    ref WordBitReader bits,
    TokenStream stream,
    int valueBits,
    int maximumBits,
    int depth,
    List<HuffmanLeaf> leaves,
    int expectedLeaves) {
    if (depth > maximumBits)
      throw this._Invalid($"token stream {(int)stream} Huffman tree exceeds its stated depth {maximumBits}");

    if (bits.Read(1, this._streamIndex, $"token stream {(int)stream} Huffman node") == 0) {
      if (leaves.Count >= expectedLeaves)
        throw this._Invalid($"token stream {(int)stream} Huffman tree has too many leaves");
      var length = depth == 0 ? 1 : depth;
      var symbol = unchecked((int)bits.Read(valueBits, this._streamIndex, $"token stream {(int)stream} Huffman literal"));
      leaves.Add(new(symbol, length));
      return length;
    }

    var left = this._ReadTreeNode(ref bits, stream, valueBits, maximumBits, depth + 1, leaves, expectedLeaves);
    var right = this._ReadTreeNode(ref bits, stream, valueBits, maximumBits, depth + 1, leaves, expectedLeaves);
    return Math.Max(left, right);
  }

  private void _ValidateToken(TokenStream stream, int token, int index) {
    if ((int)stream <= (int)TokenStream.Motion && (uint)token >= _DELTA_COUNT)
      throw this._Invalid($"token stream {(int)stream} token {index} names delta {token}, outside 0..63");
  }

  private int _NextDelta(TokenReader[] streams, TokenStream stream)
    => this._deltas[(int)stream][streams[(int)stream].NextRaw(this._streamIndex, stream)];

  private void _HighResolution(FramePlanes frame, int blockX, int blockY, TokenReader[] streams) {
    Span<int> chroma = stackalloc int[8];
    for (var i = 0; i < 4; ++i) {
      chroma[i] = this._NextDelta(streams, TokenStream.ChromaHigh);
      chroma[i + 4] = this._NextDelta(streams, TokenStream.ChromaHigh);
    }
    this._HighChroma(frame.U, blockX, blockY, this._chromaLast.AsSpan(blockX * 4, 2), this._chromaDelta.AsSpan(0, 2), chroma[..4]);
    this._HighChroma(frame.V, blockX, blockY, this._chromaLast.AsSpan(blockX * 4 + 2, 2), this._chromaDelta.AsSpan(2, 2), chroma[4..]);

    Span<int> luma = stackalloc int[16];
    for (var i = 0; i < luma.Length; ++i)
      luma[i] = this._NextDelta(streams, TokenStream.LumaHigh);
    this._ApplyLuma(frame.Y, blockX, blockY, luma, this._last.AsSpan(blockX * 4, 4));
  }

  private void _MediumResolution(FramePlanes frame, int blockX, int blockY, TokenReader[] streams) {
    Span<int> chroma = stackalloc int[4];
    chroma.Clear();
    chroma[0] = this._NextDelta(streams, TokenStream.ChromaLow);
    this._LowChroma(frame.U, blockX, blockY, this._chromaLast.AsSpan(blockX * 4, 2), this._chromaDelta.AsSpan(0, 2), chroma, blockX, 0);
    chroma.Clear();
    chroma[0] = this._NextDelta(streams, TokenStream.ChromaLow);
    this._LowChroma(frame.V, blockX, blockY, this._chromaLast.AsSpan(blockX * 4 + 2, 2), this._chromaDelta.AsSpan(2, 2), chroma, blockX, 2);

    Span<int> luma = stackalloc int[16];
    for (var i = 0; i < luma.Length; ++i)
      luma[i] = this._NextDelta(streams, TokenStream.LumaHigh);
    this._ApplyLuma(frame.Y, blockX, blockY, luma, this._last.AsSpan(blockX * 4, 4));
  }

  private void _LowResolution(FramePlanes frame, int blockX, int blockY, TokenReader[] streams) {
    Span<int> chroma = stackalloc int[4];
    chroma.Clear();
    chroma[0] = this._NextDelta(streams, TokenStream.ChromaLow);
    this._LowChroma(frame.U, blockX, blockY, this._chromaLast.AsSpan(blockX * 4, 2), this._chromaDelta.AsSpan(0, 2), chroma, blockX, 0);
    chroma.Clear();
    chroma[0] = this._NextDelta(streams, TokenStream.ChromaLow);
    this._LowChroma(frame.V, blockX, blockY, this._chromaLast.AsSpan(blockX * 4 + 2, 2), this._chromaDelta.AsSpan(2, 2), chroma, blockX, 2);

    Span<int> luma = stackalloc int[16];
    luma.Clear();
    luma[0] = this._NextDelta(streams, TokenStream.LumaLow);
    luma[2] = this._NextDelta(streams, TokenStream.LumaLow);
    luma[8] = this._NextDelta(streams, TokenStream.LumaLow);
    luma[10] = this._NextDelta(streams, TokenStream.LumaLow);

    var last = this._last.AsSpan(blockX * 4, 4);
    last[0] = blockX > 0
      ? unchecked((int)((uint)this._last[blockX * 4 - 1] - (uint)this._verticalDelta[0] - (uint)this._verticalDelta[1] - (uint)this._verticalDelta[2] - (uint)this._verticalDelta[3] + (uint)last[1])) >> 1
      : unchecked((int)((uint)last[1] - (uint)this._verticalDelta[0] - (uint)this._verticalDelta[1] - (uint)this._verticalDelta[2] - (uint)this._verticalDelta[3])) >> 1;
    last[2] = unchecked((int)((uint)last[1] + (uint)last[3])) >> 1;

    var t1 = unchecked((int)((uint)this._verticalDelta[0] + (uint)this._verticalDelta[1]));
    this._verticalDelta[0] = t1 >> 1;
    this._verticalDelta[1] = t1 - (t1 >> 1);
    var t2 = unchecked((int)((uint)this._verticalDelta[2] + (uint)this._verticalDelta[3]));
    this._verticalDelta[2] = t2 >> 1;
    this._verticalDelta[3] = t2 - (t2 >> 1);

    this._ApplyLuma(frame.Y, blockX, blockY, luma, last);
  }

  private void _NullResolution(FramePlanes frame, int blockX, int blockY) {
    Span<int> zeros = stackalloc int[4];
    zeros.Clear();
    this._LowChroma(frame.U, blockX, blockY, this._chromaLast.AsSpan(blockX * 4, 2), this._chromaDelta.AsSpan(0, 2), zeros, blockX, 0);
    this._LowChroma(frame.V, blockX, blockY, this._chromaLast.AsSpan(blockX * 4 + 2, 2), this._chromaDelta.AsSpan(2, 2), zeros, blockX, 2);

    Span<int> luma = stackalloc int[16];
    luma.Clear();
    var last = this._last.AsSpan(blockX * 4, 4);
    var ct = unchecked((int)((uint)this._verticalDelta[0] + (uint)this._verticalDelta[1] + (uint)this._verticalDelta[2] + (uint)this._verticalDelta[3]));
    var left = blockX > 0 ? unchecked((uint)this._last[blockX * 4 - 1] - (uint)ct) : 0u;
    var right = unchecked((uint)last[3]);
    var diff = unchecked((int)(right - left));
    last[0] = unchecked((int)left + (diff >> 2));
    last[1] = unchecked((int)left + (diff >> 1));
    last[2] = unchecked((int)right - (diff >> 2));
    last[3] = unchecked((int)right);

    var start = left;
    this._verticalDelta[0] = unchecked((int)(start + (uint)(ct >> 2) - left));
    left = unchecked(left + (uint)this._verticalDelta[0]);
    this._verticalDelta[1] = unchecked((int)(start + (uint)(ct >> 1) - left));
    left = unchecked(left + (uint)this._verticalDelta[1]);
    this._verticalDelta[2] = unchecked((int)((start + (uint)ct - (uint)(ct >> 2)) - left));
    left = unchecked(left + (uint)this._verticalDelta[2]);
    this._verticalDelta[3] = unchecked((int)(start + (uint)ct - left));

    this._ApplyLuma(frame.Y, blockX, blockY, luma, last);
  }

  private void _Still(FramePlanes current, FramePlanes previous, int blockX, int blockY) {
    this._CopyBlock(previous.U, current.U, this._chromaWidth, blockX * 2, blockY * 2, 2, 2);
    this._CopyBlock(previous.V, current.V, this._chromaWidth, blockX * 2, blockY * 2, 2, 2);
    this._RecalculateChroma(current.U, blockX, blockY, this._chromaLast.AsSpan(blockX * 4, 2), this._chromaDelta.AsSpan(0, 2));
    this._RecalculateChroma(current.V, blockX, blockY, this._chromaLast.AsSpan(blockX * 4 + 2, 2), this._chromaDelta.AsSpan(2, 2));

    var x = blockX * 4;
    var y = blockY * 4;
    var last = this._last.AsSpan(x, 4);
    this._verticalDelta[0] = previous.Y[y * this._width + x + 3] - last[3];
    this._verticalDelta[1] = previous.Y[(y + 1) * this._width + x + 3] - previous.Y[y * this._width + x + 3];
    this._verticalDelta[2] = previous.Y[(y + 2) * this._width + x + 3] - previous.Y[(y + 1) * this._width + x + 3];
    this._verticalDelta[3] = previous.Y[(y + 3) * this._width + x + 3] - previous.Y[(y + 2) * this._width + x + 3];
    this._CopyBlock(previous.Y, current.Y, this._width, x, y, 4, 4);
    for (var i = 0; i < 4; ++i)
      last[i] = previous.Y[(y + 3) * this._width + x + i];
  }

  private void _Update(FramePlanes current, FramePlanes previous, int blockX, int blockY, TokenReader[] streams) {
    var cx = blockX * 2;
    var cy = blockY * 2;
    for (var row = 0; row < 2; ++row)
      for (var column = 0; column < 2; ++column) {
        var at = (cy + row) * this._chromaWidth + cx + column;
        current.U[at] = previous.U[at] + this._NextDelta(streams, TokenStream.Update);
        current.V[at] = previous.V[at] + this._NextDelta(streams, TokenStream.Update);
      }
    this._RecalculateChroma(current.U, blockX, blockY, this._chromaLast.AsSpan(blockX * 4, 2), this._chromaDelta.AsSpan(0, 2));
    this._RecalculateChroma(current.V, blockX, blockY, this._chromaLast.AsSpan(blockX * 4 + 2, 2), this._chromaDelta.AsSpan(2, 2));

    var x = blockX * 4;
    var y = blockY * 4;
    var last = this._last.AsSpan(x, 4);
    this._verticalDelta[0] = previous.Y[y * this._width + x + 3] - last[3];
    this._verticalDelta[1] = previous.Y[(y + 1) * this._width + x + 3] - previous.Y[y * this._width + x + 3];
    this._verticalDelta[2] = previous.Y[(y + 2) * this._width + x + 3] - previous.Y[(y + 1) * this._width + x + 3];
    this._verticalDelta[3] = previous.Y[(y + 3) * this._width + x + 3] - previous.Y[(y + 2) * this._width + x + 3];

    for (var row = 0; row < 4; ++row) {
      var oldRight = last[3];
      for (var column = 0; column < 4; ++column) {
        var at = (y + row) * this._width + x + column;
        current.Y[at] = previous.Y[at] + this._NextDelta(streams, TokenStream.Update);
        last[column] = current.Y[at];
      }
      this._verticalDelta[row] = last[3] - oldRight;
    }
  }

  private void _Motion(FramePlanes current, FramePlanes previous, int blockX, int blockY, TokenReader[] streams) {
    var motionX = this._NextDelta(streams, TokenStream.Motion);
    var motionY = this._NextDelta(streams, TokenStream.Motion);
    motionX = Math.Clamp(motionX, -(blockX * 4 + 4), this._width - blockX * 4);
    motionY = Math.Clamp(motionY, -(blockY * 4 + 4), this._height - blockY * 4);

    var sourceX = blockX * 4 + motionX;
    var sourceY = blockY * 4 + motionY;
    if (sourceX < 0 || sourceY < 0 || sourceX + 4 > this._width || sourceY + 4 > this._height)
      throw this._Invalid($"motion block {blockX},{blockY} points outside the previous picture ({motionX},{motionY})");

    this._CopyBlock(previous.Y, current.Y, this._width, sourceX, sourceY, 4, 4, blockX * 4, blockY * 4);
    this._CopyBlock(previous.U, current.U, this._chromaWidth, sourceX >> 1, sourceY >> 1, 2, 2, blockX * 2, blockY * 2);
    this._CopyBlock(previous.V, current.V, this._chromaWidth, sourceX >> 1, sourceY >> 1, 2, 2, blockX * 2, blockY * 2);
    this._RecalculateChroma(current.U, blockX, blockY, this._chromaLast.AsSpan(blockX * 4, 2), this._chromaDelta.AsSpan(0, 2));
    this._RecalculateChroma(current.V, blockX, blockY, this._chromaLast.AsSpan(blockX * 4 + 2, 2), this._chromaDelta.AsSpan(2, 2));

    var x = blockX * 4;
    var y = blockY * 4;
    var last = this._last.AsSpan(x, 4);
    this._verticalDelta[0] = current.Y[y * this._width + x + 3] - last[3];
    this._verticalDelta[1] = current.Y[(y + 1) * this._width + x + 3] - current.Y[y * this._width + x + 3];
    this._verticalDelta[2] = current.Y[(y + 2) * this._width + x + 3] - current.Y[(y + 1) * this._width + x + 3];
    this._verticalDelta[3] = current.Y[(y + 3) * this._width + x + 3] - current.Y[(y + 2) * this._width + x + 3];
    for (var i = 0; i < 4; ++i)
      last[i] = current.Y[(y + 3) * this._width + x + i];
  }

  private void _ApplyLuma(int[] plane, int blockX, int blockY, ReadOnlySpan<int> deltas, Span<int> last) {
    var x = blockX * 4;
    var y = blockY * 4;
    for (var row = 0; row < 4; ++row) {
      var accumulated = unchecked((uint)this._verticalDelta[row]);
      for (var column = 0; column < 4; ++column) {
        accumulated = unchecked(accumulated + (uint)deltas[column + row * 4]);
        last[column] = unchecked(last[column] + (int)accumulated);
        plane[(y + row) * this._width + x + column] = _ClipByte(last[column]);
      }
      this._verticalDelta[row] = unchecked((int)accumulated);
    }
  }

  private void _HighChroma(int[] plane, int blockX, int blockY, Span<int> last, Span<int> cd, ReadOnlySpan<int> deltas) {
    var x = blockX * 2;
    var y = blockY * 2;
    for (var row = 0; row < 2; ++row)
      for (var column = 0; column < 2; ++column) {
        cd[row] = unchecked(cd[row] + deltas[column + row * 2]);
        last[column] = unchecked(last[column] + cd[row]);
        plane[(y + row) * this._chromaWidth + x + column] = last[column];
      }
  }

  private void _LowChroma(int[] plane, int blockX, int blockY, Span<int> last, Span<int> cd, ReadOnlySpan<int> deltas, int bx, int componentOffset) {
    var previous = bx > 0 ? this._chromaLast[blockX * 4 + componentOffset - 3] : 0;
    var t = unchecked((int)((uint)cd[0] + (uint)cd[1])) >> 1;
    var l = unchecked((int)((uint)previous - (uint)cd[0] - (uint)cd[1] + (uint)last[1])) >> 1;
    cd[1] = unchecked(cd[0] + cd[1] - t);
    cd[0] = t;
    last[0] = l;
    this._HighChroma(plane, blockX, blockY, last, cd, deltas);
  }

  private void _RecalculateChroma(int[] plane, int blockX, int blockY, Span<int> last, Span<int> cd) {
    var x = blockX * 2;
    var y = blockY * 2;
    var topRight = plane[y * this._chromaWidth + x + 1];
    var bottomLeft = plane[(y + 1) * this._chromaWidth + x];
    var bottomRight = plane[(y + 1) * this._chromaWidth + x + 1];
    cd[0] = topRight - last[1];
    cd[1] = bottomRight - topRight;
    last[0] = bottomLeft;
    last[1] = bottomRight;
  }

  private RawImage _ToRawImage(FramePlanes frame) {
    var pixels = new byte[checked(this._width * this._height * 3)];
    for (var y = 0; y < this._height; ++y)
      for (var x = 0; x < this._width; ++x) {
        var luma = frame.Y[y * this._width + x];
        var chromaAt = (y >> 1) * this._chromaWidth + (x >> 1);
        var u = frame.U[chromaAt];
        var v = frame.V[chromaAt];
        var at = (y * this._width + x) * 3;
        pixels[at] = (byte)_ClipByte(luma + v);
        pixels[at + 1] = (byte)_ClipByte(luma);
        pixels[at + 2] = (byte)_ClipByte(luma + u);
      }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Bgr24, PixelData = pixels };
  }

  private void _CopyBlock(int[] source, int[] destination, int stride, int sourceX, int sourceY, int width, int height)
    => this._CopyBlock(source, destination, stride, sourceX, sourceY, width, height, sourceX, sourceY);

  private void _CopyBlock(
    int[] source, int[] destination, int stride,
    int sourceX, int sourceY, int width, int height,
    int destinationX, int destinationY) {
    for (var row = 0; row < height; ++row)
      Array.Copy(source, (sourceY + row) * stride + sourceX, destination, (destinationY + row) * stride + destinationX, width);
  }

  private InvalidDataException _Invalid(string message) => new($"TrueMotion 2 stream {this._streamIndex}: {message}.");

  private static int[][] _CreateDeltaTables() {
    var result = new int[_STREAM_COUNT][];
    for (var i = 0; i < result.Length; ++i)
      result[i] = new int[_DELTA_COUNT];
    return result;
  }

  private static int _ClipByte(int value) => Math.Clamp(value, byte.MinValue, byte.MaxValue);

  private static uint _ReadUInt32(ReadOnlySpan<byte> data, ref int position, int streamIndex, string field) {
    if (position > data.Length - 4)
      throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: {field} runs past the token stream.");
    var value = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
    position += 4;
    return value;
  }

  private static void _Skip(ReadOnlySpan<byte> data, ref int position, int count, int streamIndex, string field) {
    if (position > data.Length - count)
      throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: {field} runs past the token stream.");
    position += count;
  }

  private readonly record struct ParsedStream(int BytesConsumed, int[] Tokens);
  private readonly record struct HuffmanLeaf(int Symbol, int Length);

  private sealed class FramePlanes(int width, int height) {
    internal int[] Y { get; } = new int[checked(width * height)];
    internal int[] U { get; } = new int[checked((width >> 1) * (height >> 1))];
    internal int[] V { get; } = new int[checked((width >> 1) * (height >> 1))];
  }

  private sealed class TokenReader(int[] tokens) {
    private int _position;
    internal int Remaining => tokens.Length - this._position;

    internal int NextRaw(int streamIndex, TokenStream stream) {
      if ((uint)this._position >= (uint)tokens.Length)
        throw new InvalidDataException(
          $"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} ended at token {this._position}.");
      return tokens[this._position++];
    }
  }

  private sealed class HuffmanDecoder {
    private readonly Node _root;
    internal int FirstSymbol { get; }

    private HuffmanDecoder(Node root, int firstSymbol) {
      this._root = root;
      this.FirstSymbol = firstSymbol;
    }

    internal static HuffmanDecoder Build(IReadOnlyList<HuffmanLeaf> leaves, int streamIndex, TokenStream stream) {
      if (leaves.Count == 0)
        throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} has no Huffman leaves.");

      var root = new Node();
      ulong code = 0;
      foreach (var leaf in leaves) {
        var length = leaf.Length;
        if (length is < 1 or > 32)
          throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} has code length {length}.");
        var unit = 1UL << (32 - length);
        if ((code & (unit - 1)) != 0 || code > uint.MaxValue)
          throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} has a non-canonical leaf order.");
        _Insert(root, (uint)code, length, leaf.Symbol, streamIndex, stream);
        code += unit;
        if (code > 0x1_0000_0000UL)
          throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} overdetermines its Huffman code space.");
      }

      return new(root, leaves[0].Symbol);
    }

    internal int Decode(ref WordBitReader bits, int streamIndex, TokenStream stream) {
      var node = this._root;
      for (var depth = 0; depth < 32; ++depth) {
        if (node.HasSymbol)
          return node.Symbol;
        var bit = bits.Read(1, streamIndex, $"token stream {(int)stream} Huffman code");
        node = bit == 0 ? node.Zero : node.One;
        if (node == null)
          throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} contains an invalid Huffman code.");
      }

      if (node.HasSymbol)
        return node.Symbol;
      throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} contains a Huffman code longer than 32 bits.");
    }

    private static void _Insert(Node root, uint code, int length, int symbol, int streamIndex, TokenStream stream) {
      var node = root;
      for (var depth = 0; depth < length; ++depth) {
        if (node.HasSymbol)
          throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} has a Huffman prefix collision.");
        var one = ((code >> (31 - depth)) & 1) != 0;
        if (one)
          node = node.One ??= new();
        else
          node = node.Zero ??= new();
      }
      if (node.HasSymbol || node.Zero != null || node.One != null)
        throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: token stream {(int)stream} has duplicate/overlapping Huffman codes.");
      node.Symbol = symbol;
      node.HasSymbol = true;
    }

    private sealed class Node {
      internal Node? Zero;
      internal Node? One;
      internal int Symbol;
      internal bool HasSymbol;
    }
  }

  private ref struct WordBitReader(ReadOnlySpan<byte> data) {
    private int _bitPosition;
    internal int AlignedBytesConsumed => (this._bitPosition + 31) / 32 * 4;

    internal uint Read(int count, int streamIndex, string field) {
      if (count is < 0 or > 32)
        throw new ArgumentOutOfRangeException(nameof(count));
      if (this._bitPosition > data.Length * 8 - count)
        throw new InvalidDataException($"TrueMotion 2 stream {streamIndex}: {field} runs past the available bits.");

      uint value = 0;
      for (var i = 0; i < count; ++i) {
        var wordOffset = (this._bitPosition >> 5) * 4;
        var bitInWord = this._bitPosition & 31;
        var word = BinaryPrimitives.ReadUInt32LittleEndian(data[wordOffset..]);
        value = (value << 1) | ((word >> (31 - bitInWord)) & 1);
        ++this._bitPosition;
      }
      return value;
    }
  }
}
