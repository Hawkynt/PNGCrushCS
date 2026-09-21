using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Duck/On2 TrueMotion 2 (<c>TM20</c>) pictures as spatial high-resolution key frames and
/// previous-picture update/still P frames.
/// </summary>
/// <remarks>
/// The write path is the inverse of the LGPL-compatible TrueMotion 2 decoder adapted in
/// <see cref="TrueMotion2VideoDecoder"/>. FFmpeg has no TrueMotion 2 encoder, so no external encoder
/// implementation is translated here; FFmpeg's decoder is instead the interoperability oracle.
/// <para/>
/// The format has no B pictures or future references. Every twelfth picture is written as a key frame
/// using high-resolution blocks. Pictures between them use STILL for an unchanged 4x4 block and UPDATE
/// otherwise. UPDATE is sufficient to represent any change against the previous reconstruction;
/// motion blocks are a compression choice, not a required write form, and are therefore left to the
/// decoder rather than guessed at by a shallow motion search.
/// <para/>
/// Each active value stream carries an explicit signed delta table and a complete fixed-depth Huffman
/// tree. Up to 64 distinct deltas are represented exactly; once a frame needs more, the nearest delta
/// already present is used and the writer continues prediction from the value a decoder reconstructs,
/// so loss does not drift invisibly between the encoder and its reference picture.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class TrueMotion2VideoEncoder : IVideoCodecEncoder<TrueMotion2VideoEncoder> {

  private enum TokenStream { ChromaHigh, ChromaLow, LumaHigh, LumaLow, Update, Motion, Type }
  private enum BlockType { HighResolution, MediumResolution, LowResolution, NullResolution, Update, Still, Motion }

  private const int _HEADER_SIZE = 40;
  private const int _KEYFRAME_INTERVAL = 12;
  private const uint _MAGIC = 0x00000101;
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("TM20");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private FramePlanes? _reference;
  private int _pictureNumber;

  private TrueMotion2VideoEncoder(MediaStreamInfo requested) {
    this._width = requested.Width;
    this._height = requested.Height;
    this._chromaWidth = requested.Width >> 1;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: requested.Width,
      Height: requested.Height,
      Planes: 1,
      BitsPerPixel: 24,
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
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public static string CodecName => "Duck TrueMotion 2";
  public static CodecTag Codec => _Tag;

  public static TrueMotion2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Duck TrueMotion 2 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"TrueMotion 2 needs a positive picture size; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    if ((stream.Width & 3) != 0 || (stream.Height & 3) != 0)
      throw new NotSupportedException(
        $"TrueMotion 2 codes 4x4 blocks and requires both dimensions to be multiples of four; stream {stream.Index} "
        + $"states {stream.Width}x{stream.Height}.");
    if ((long)stream.Width * stream.Height > int.MaxValue / 3)
      throw new NotSupportedException(
        $"TrueMotion 2 stream {stream.Index} is {stream.Width}x{stream.Height}, too large for an in-memory RGB frame.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"TrueMotion 2 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var target = this._TargetPlanes(frame);
    var keyFrame = this._reference == null || this._pictureNumber % _KEYFRAME_INTERVAL == 0;
    var encoded = keyFrame ? this._EncodeKeyFrame(target) : this._EncodePredictedFrame(target, this._reference!);
    this._reference = encoded.Reconstruction;
    ++this._pictureNumber;

    packet = new(
      this._stream.Index,
      encoded.Packet,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: keyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private EncodedFrame _EncodeKeyFrame(FramePlanes target) {
    var current = new FramePlanes(this._width, this._height);
    var chroma = new DeltaBuilder();
    var luma = new DeltaBuilder();
    var typeTokens = new List<int>(this._width * this._height / 16);
    var last = new int[this._width];
    var chromaLast = new int[this._width];
    Span<int> d = stackalloc int[4];
    Span<int> cd = stackalloc int[4];

    for (var blockY = 0; blockY < this._height / 4; ++blockY) {
      d.Clear();
      cd.Clear();
      for (var blockX = 0; blockX < this._width / 4; ++blockX) {
        typeTokens.Add((int)BlockType.HighResolution);
        var cx = blockX * 2;
        var cy = blockY * 2;
        var cbase = blockX * 4;
        for (var position = 0; position < 4; ++position) {
          var row = position >> 1;
          var column = position & 1;
          this._EncodeHighChromaSample(
            target.U[(cy + row) * this._chromaWidth + cx + column],
            current.U, (cy + row) * this._chromaWidth + cx + column,
            chromaLast, cbase, cd, 0, row, column, chroma);
          this._EncodeHighChromaSample(
            target.V[(cy + row) * this._chromaWidth + cx + column],
            current.V, (cy + row) * this._chromaWidth + cx + column,
            chromaLast, cbase + 2, cd, 2, row, column, chroma);
        }

        var x = blockX * 4;
        var y = blockY * 4;
        for (var row = 0; row < 4; ++row) {
          var accumulated = d[row];
          for (var column = 0; column < 4; ++column) {
            var desired = target.Y[(y + row) * this._width + x + column];
            var rawDelta = desired - last[x + column] - accumulated;
            var chosen = luma.Choose(rawDelta);
            accumulated = unchecked(accumulated + chosen.Value);
            last[x + column] = unchecked(last[x + column] + accumulated);
            current.Y[(y + row) * this._width + x + column] = Math.Clamp(last[x + column], 0, 255);
          }
          d[row] = accumulated;
        }
      }
    }

    var streams = new byte[7][];
    streams[(int)TokenStream.ChromaHigh] = _WriteDeltaStream(chroma);
    streams[(int)TokenStream.ChromaLow] = _WriteEmptyStream();
    streams[(int)TokenStream.LumaHigh] = _WriteDeltaStream(luma);
    streams[(int)TokenStream.LumaLow] = _WriteEmptyStream();
    streams[(int)TokenStream.Update] = _WriteEmptyStream();
    streams[(int)TokenStream.Motion] = _WriteEmptyStream();
    streams[(int)TokenStream.Type] = _WriteTypeStream(typeTokens);
    return new(_Frame(streams), current);
  }

  private EncodedFrame _EncodePredictedFrame(FramePlanes target, FramePlanes previous) {
    var current = new FramePlanes(this._width, this._height);
    var update = new DeltaBuilder();
    var typeTokens = new List<int>(this._width * this._height / 16);
    var last = new int[this._width];
    var chromaLast = new int[this._width];
    Span<int> d = stackalloc int[4];
    Span<int> cd = stackalloc int[4];

    for (var blockY = 0; blockY < this._height / 4; ++blockY) {
      d.Clear();
      cd.Clear();
      for (var blockX = 0; blockX < this._width / 4; ++blockX) {
        if (this._BlockEquals(target, previous, blockX, blockY)) {
          typeTokens.Add((int)BlockType.Still);
          this._CopyStillBlock(current, previous, blockX, blockY, last, chromaLast, d, cd);
          continue;
        }

        typeTokens.Add((int)BlockType.Update);
        this._EncodeUpdateBlock(target, current, previous, blockX, blockY, last, chromaLast, d, cd, update);
      }
    }

    var streams = new byte[7][];
    streams[(int)TokenStream.ChromaHigh] = _WriteEmptyStream();
    streams[(int)TokenStream.ChromaLow] = _WriteEmptyStream();
    streams[(int)TokenStream.LumaHigh] = _WriteEmptyStream();
    streams[(int)TokenStream.LumaLow] = _WriteEmptyStream();
    streams[(int)TokenStream.Update] = _WriteDeltaStream(update);
    streams[(int)TokenStream.Motion] = _WriteEmptyStream();
    streams[(int)TokenStream.Type] = _WriteTypeStream(typeTokens);
    return new(_Frame(streams), current);
  }

  private void _EncodeHighChromaSample(
    int desired,
    int[] plane,
    int planeIndex,
    int[] last,
    int lastBase,
    Span<int> cd,
    int cdBase,
    int row,
    int column,
    DeltaBuilder builder) {
    var rawDelta = desired - last[lastBase + column] - cd[cdBase + row];
    var chosen = builder.Choose(rawDelta);
    cd[cdBase + row] = unchecked(cd[cdBase + row] + chosen.Value);
    last[lastBase + column] = unchecked(last[lastBase + column] + cd[cdBase + row]);
    plane[planeIndex] = last[lastBase + column];
  }

  private void _EncodeUpdateBlock(
    FramePlanes target,
    FramePlanes current,
    FramePlanes previous,
    int blockX,
    int blockY,
    int[] last,
    int[] chromaLast,
    Span<int> d,
    Span<int> cd,
    DeltaBuilder update) {
    var cx = blockX * 2;
    var cy = blockY * 2;
    for (var row = 0; row < 2; ++row)
      for (var column = 0; column < 2; ++column) {
        var at = (cy + row) * this._chromaWidth + cx + column;
        var u = update.Choose(target.U[at] - previous.U[at]);
        current.U[at] = previous.U[at] + u.Value;
        var v = update.Choose(target.V[at] - previous.V[at]);
        current.V[at] = previous.V[at] + v.Value;
      }
    this._RecalculateChroma(current.U, blockX, blockY, chromaLast.AsSpan(blockX * 4, 2), cd[..2]);
    this._RecalculateChroma(current.V, blockX, blockY, chromaLast.AsSpan(blockX * 4 + 2, 2), cd[2..]);

    var x = blockX * 4;
    var y = blockY * 4;
    d[0] = previous.Y[y * this._width + x + 3] - last[x + 3];
    d[1] = previous.Y[(y + 1) * this._width + x + 3] - previous.Y[y * this._width + x + 3];
    d[2] = previous.Y[(y + 2) * this._width + x + 3] - previous.Y[(y + 1) * this._width + x + 3];
    d[3] = previous.Y[(y + 3) * this._width + x + 3] - previous.Y[(y + 2) * this._width + x + 3];

    for (var row = 0; row < 4; ++row) {
      var oldRight = last[x + 3];
      for (var column = 0; column < 4; ++column) {
        var at = (y + row) * this._width + x + column;
        var delta = update.Choose(target.Y[at] - previous.Y[at]);
        current.Y[at] = previous.Y[at] + delta.Value;
        last[x + column] = current.Y[at];
      }
      d[row] = last[x + 3] - oldRight;
    }
  }

  private void _CopyStillBlock(
    FramePlanes current,
    FramePlanes previous,
    int blockX,
    int blockY,
    int[] last,
    int[] chromaLast,
    Span<int> d,
    Span<int> cd) {
    var cx = blockX * 2;
    var cy = blockY * 2;
    _CopyBlock(previous.U, current.U, this._chromaWidth, cx, cy, 2, 2);
    _CopyBlock(previous.V, current.V, this._chromaWidth, cx, cy, 2, 2);
    this._RecalculateChroma(current.U, blockX, blockY, chromaLast.AsSpan(blockX * 4, 2), cd[..2]);
    this._RecalculateChroma(current.V, blockX, blockY, chromaLast.AsSpan(blockX * 4 + 2, 2), cd[2..]);

    var x = blockX * 4;
    var y = blockY * 4;
    d[0] = previous.Y[y * this._width + x + 3] - last[x + 3];
    d[1] = previous.Y[(y + 1) * this._width + x + 3] - previous.Y[y * this._width + x + 3];
    d[2] = previous.Y[(y + 2) * this._width + x + 3] - previous.Y[(y + 1) * this._width + x + 3];
    d[3] = previous.Y[(y + 3) * this._width + x + 3] - previous.Y[(y + 2) * this._width + x + 3];
    _CopyBlock(previous.Y, current.Y, this._width, x, y, 4, 4);
    for (var column = 0; column < 4; ++column)
      last[x + column] = previous.Y[(y + 3) * this._width + x + column];
  }

  private bool _BlockEquals(FramePlanes left, FramePlanes right, int blockX, int blockY) {
    var x = blockX * 4;
    var y = blockY * 4;
    for (var row = 0; row < 4; ++row)
      for (var column = 0; column < 4; ++column)
        if (left.Y[(y + row) * this._width + x + column] != right.Y[(y + row) * this._width + x + column])
          return false;

    var cx = blockX * 2;
    var cy = blockY * 2;
    for (var row = 0; row < 2; ++row)
      for (var column = 0; column < 2; ++column) {
        var at = (cy + row) * this._chromaWidth + cx + column;
        if (left.U[at] != right.U[at] || left.V[at] != right.V[at])
          return false;
      }
    return true;
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

  private FramePlanes _TargetPlanes(RawImage frame) {
    var bgr = frame.Format == PixelFormat.Bgr24 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Bgr24);
    var target = new FramePlanes(this._width, this._height);
    for (var y = 0; y < this._height; ++y)
      for (var x = 0; x < this._width; ++x) {
        var at = (y * this._width + x) * 3;
        target.Y[y * this._width + x] = bgr.PixelData[at + 1];
      }

    for (var y = 0; y < this._height; y += 2)
      for (var x = 0; x < this._width; x += 2) {
        var redDifference = 0;
        var blueDifference = 0;
        for (var dy = 0; dy < 2; ++dy)
          for (var dx = 0; dx < 2; ++dx) {
            var at = ((y + dy) * this._width + x + dx) * 3;
            var blue = bgr.PixelData[at];
            var green = bgr.PixelData[at + 1];
            var red = bgr.PixelData[at + 2];
            redDifference += red - green;
            blueDifference += blue - green;
          }
        var chromaAt = (y >> 1) * this._chromaWidth + (x >> 1);
        target.U[chromaAt] = _RoundQuarter(redDifference);
        target.V[chromaAt] = _RoundQuarter(blueDifference);
      }

    return target;
  }

  private static int _RoundQuarter(int value)
    => value >= 0 ? (value + 2) >> 2 : -((-value + 2) >> 2);

  private static byte[] _Frame(IReadOnlyList<byte[]> streams) {
    var length = _HEADER_SIZE;
    foreach (var stream in streams)
      length = checked(length + stream.Length);
    var packet = new byte[length];
    BinaryPrimitives.WriteUInt32BigEndian(packet, _MAGIC);
    var offset = _HEADER_SIZE;
    foreach (var stream in streams) {
      stream.CopyTo(packet, offset);
      offset += stream.Length;
    }
    return packet;
  }

  private static byte[] _WriteDeltaStream(DeltaBuilder builder) {
    var values = builder.Values;
    if (values.Count == 0)
      return _WriteEmptyStream();

    var valueBits = _SignedWidth(values);
    var deltaBits = new WordBitWriter();
    deltaBits.Write((uint)values.Count, 9);
    deltaBits.Write((uint)valueBits, 5);
    foreach (var value in values)
      deltaBits.Write(_TwosComplement(value, valueBits), valueBits);
    var deltaBytes = deltaBits.ToArray();

    return _WriteStream(
      builder.Tokens,
      hasDeltaTable: true,
      deltaBytes,
      symbolBits: 6,
      codeBits: 6,
      alphabetSize: 64);
  }

  private static byte[] _WriteTypeStream(IReadOnlyList<int> tokens)
    => _WriteStream(tokens, hasDeltaTable: false, [], symbolBits: 3, codeBits: 3, alphabetSize: 8);

  private static byte[] _WriteEmptyStream() {
    var tree = new WordBitWriter();
    tree.Write(1, 5);
    tree.Write(0, 5);
    tree.Write(0, 5);
    tree.Write(1, 17);
    tree.Write(0, 1);
    tree.Write(0, 1);
    var treeBytes = tree.ToArray();

    using var output = new MemoryStream();
    _WriteUInt32(output, 0);
    _WriteUInt32(output, 0);
    _WriteUInt32(output, (uint)(treeBytes.Length / 4));
    _WriteUInt32(output, 0);
    output.Write(treeBytes);
    _WriteUInt32(output, 0);
    var result = output.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)((result.Length - 4) / 4));
    return result;
  }

  private static byte[] _WriteStream(
    IReadOnlyList<int> tokens,
    bool hasDeltaTable,
    ReadOnlySpan<byte> deltaBytes,
    int symbolBits,
    int codeBits,
    int alphabetSize) {
    using var output = new MemoryStream();
    _WriteUInt32(output, 0);
    _WriteUInt32(output, checked((uint)tokens.Count << 1) | (hasDeltaTable ? 1u : 0u));
    if (hasDeltaTable) {
      _WriteUInt32(output, checked((uint)(deltaBytes.Length / 4)));
      output.Write(deltaBytes);
    }

    var tree = new WordBitWriter();
    tree.Write((uint)symbolBits, 5);
    tree.Write((uint)codeBits, 5);
    tree.Write((uint)codeBits, 5);
    tree.Write((uint)(alphabetSize * 2 - 1), 17);
    _WriteBalancedTree(tree, 0, alphabetSize, symbolBits);
    var treeBytes = tree.ToArray();
    _WriteUInt32(output, checked((uint)(treeBytes.Length / 4)));
    _WriteUInt32(output, 0);
    output.Write(treeBytes);

    var tokenBits = new WordBitWriter();
    foreach (var token in tokens)
      tokenBits.Write(checked((uint)token), codeBits);
    var tokenBytes = tokenBits.ToArray();
    _WriteUInt32(output, checked((uint)(tokenBytes.Length / 4)));
    output.Write(tokenBytes);

    var result = output.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)((result.Length - 4) / 4)));
    return result;
  }

  private static void _WriteBalancedTree(WordBitWriter writer, int firstSymbol, int count, int symbolBits) {
    if (count == 1) {
      writer.Write(0, 1);
      writer.Write(checked((uint)firstSymbol), symbolBits);
      return;
    }

    writer.Write(1, 1);
    var half = count >> 1;
    _WriteBalancedTree(writer, firstSymbol, half, symbolBits);
    _WriteBalancedTree(writer, firstSymbol + half, half, symbolBits);
  }

  private static int _SignedWidth(IReadOnlyList<int> values) {
    for (var bits = 1; bits <= 31; ++bits) {
      var minimum = -(1L << (bits - 1));
      var maximum = (1L << (bits - 1)) - 1;
      var fits = true;
      foreach (var value in values)
        if (value < minimum || value > maximum) {
          fits = false;
          break;
        }
      if (fits)
        return bits;
    }
    throw new InvalidDataException("A TrueMotion 2 delta requires more than the format's 31 signed bits.");
  }

  private static uint _TwosComplement(int value, int bits)
    => bits == 32 ? unchecked((uint)value) : unchecked((uint)value) & ((1u << bits) - 1);

  private static void _WriteUInt32(Stream stream, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    stream.Write(bytes);
  }

  private static void _CopyBlock(int[] source, int[] destination, int stride, int x, int y, int width, int height) {
    for (var row = 0; row < height; ++row)
      Array.Copy(source, (y + row) * stride + x, destination, (y + row) * stride + x, width);
  }

  private readonly record struct EncodedFrame(byte[] Packet, FramePlanes Reconstruction);
  private readonly record struct DeltaChoice(int Index, int Value);

  private sealed class DeltaBuilder {
    private readonly List<int> _values = [];
    private readonly List<int> _tokens = [];
    internal IReadOnlyList<int> Values => this._values;
    internal IReadOnlyList<int> Tokens => this._tokens;

    internal DeltaChoice Choose(int desired) {
      var index = this._values.IndexOf(desired);
      if (index < 0 && this._values.Count < 64) {
        index = this._values.Count;
        this._values.Add(desired);
      }

      if (index < 0) {
        var bestDistance = long.MaxValue;
        for (var i = 0; i < this._values.Count; ++i) {
          var distance = Math.Abs((long)desired - this._values[i]);
          if (distance >= bestDistance)
            continue;
          bestDistance = distance;
          index = i;
        }
      }

      this._tokens.Add(index);
      return new(index, this._values[index]);
    }
  }

  private sealed class FramePlanes(int width, int height) {
    internal int[] Y { get; } = new int[checked(width * height)];
    internal int[] U { get; } = new int[checked((width >> 1) * (height >> 1))];
    internal int[] V { get; } = new int[checked((width >> 1) * (height >> 1))];
  }

  private sealed class WordBitWriter {
    private readonly List<uint> _words = [];
    private uint _word;
    private int _bits;

    internal void Write(uint value, int count) {
      if (count is < 0 or > 32)
        throw new ArgumentOutOfRangeException(nameof(count));
      if (count < 32 && value >= (1u << count))
        throw new ArgumentOutOfRangeException(nameof(value), value, $"Value does not fit in {count} bits.");

      for (var bit = count - 1; bit >= 0; --bit) {
        this._word |= ((value >> bit) & 1) << (31 - this._bits);
        if (++this._bits != 32)
          continue;
        this._words.Add(this._word);
        this._word = 0;
        this._bits = 0;
      }
    }

    internal byte[] ToArray() {
      var count = this._words.Count + (this._bits == 0 ? 0 : 1);
      var result = new byte[count * 4];
      var offset = 0;
      foreach (var word in this._words) {
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), word);
        offset += 4;
      }
      if (this._bits != 0)
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), this._word);
      return result;
    }
  }
}
