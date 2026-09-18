using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes the Canopus Lossless Codec (<c>CLLC</c>).</summary>
/// <remarks>
/// Canopus did not publish a bitstream specification or an encoder implementation. This writer is
/// the independently derived inverse of the public decoder behaviour: each frame carries canonical
/// Huffman tables followed by byte-valued predictive deltas, with every 16-bit word byte-swapped on
/// the wire. FFmpeg's LGPL-2.1-or-later <c>libavcodec/cllc.c</c> is used as the external decoder
/// oracle; no encoder code exists there to copy.
/// <para/>
/// CLLC is intra-only. Coding type 0 stores planar 8-bit YUV 4:2:2, type 1 stores RGB24 and type 3
/// stores ARGB32; coding type 2 is the second historical RGB spelling and is read but need not be
/// written. Every packet is therefore a key frame and has no forward or backward reference.
/// <para/>
/// The YUV writer accepts native <see cref="PixelFormat.Yuv422P8"/> only, because converting RGB to
/// subsampled YUV would make a lossless codec silently lossy. RGB and ARGB accept the eight-bit
/// colour representations that <see cref="LosslessEncoderInput"/> can reorder without changing
/// sample values. ARGB cannot preserve hidden RGB under alpha zero because the format omits those
/// three symbols; such pixels are refused rather than changed.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class CanopusLosslessVideoEncoder : IVideoCodecEncoder<CanopusLosslessVideoEncoder> {

  private const int _MAX_CODE_LENGTH = 14;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CLLC");

  private readonly MediaStreamInfo _stream;
  private readonly CodingMode _mode;

  private CanopusLosslessVideoEncoder(MediaStreamInfo stream, CodingMode mode) {
    this._mode = mode;

    var bitsPerPixel = mode switch {
      CodingMode.Yuv422 => 16,
      CodingMode.Rgb24 => 24,
      CodingMode.Argb32 => 32,
      _ => throw new InvalidOperationException($"Unknown CLLC coding mode {mode}."),
    };

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: checked((short)bitsPerPixel),
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Canopus Lossless Codec";

  public static CodecTag Codec => _Tag;

  public static CanopusLosslessVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Canopus Lossless Codec can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A CLLC encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if ((long)stream.Width * stream.Height * 4 > int.MaxValue)
      throw new NotSupportedException($"A {stream.Width}x{stream.Height} picture is too large for a CLLC frame.");

    var mode = stream.BitsPerPixel switch {
      0 or 24 => CodingMode.Rgb24,
      16 => CodingMode.Yuv422,
      32 => CodingMode.Argb32,
      _ => throw new NotSupportedException(
        $"CLLC writes 16-bit YUV 4:2:2, RGB24 or ARGB32; stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel."),
    };

    if (mode == CodingMode.Yuv422 && (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"CLLC YUV 4:2:2 needs an even width; {stream.Width} pixels would leave an unpaired chroma sample.");

    return new(stream, mode);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    var data = this._mode switch {
      CodingMode.Yuv422 => this._EncodeYuv422(frame),
      CodingMode.Rgb24 => this._EncodeRgb24(frame),
      CodingMode.Argb32 => this._EncodeArgb(frame),
      _ => throw new InvalidOperationException($"Unknown CLLC coding mode {this._mode}."),
    };

    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private byte[] _EncodeRgb24(RawImage frame) {
    var picture = LosslessEncoderInput.Prepare(
      frame, PixelFormat.Rgb24, this._stream.Width, this._stream.Height, CodecName);
    var pixels = picture.PixelData.AsSpan();
    var pixelCount = checked(this._stream.Width * this._stream.Height);
    var residuals = new[] { new byte[pixelCount], new byte[pixelCount], new byte[pixelCount] };
    Span<int> topLeft = stackalloc int[] { 128, 128, 128 };

    for (var y = 0; y < this._stream.Height; ++y) {
      var rowAt = y * this._stream.Width * 3;
      var residualAt = y * this._stream.Width;
      for (var component = 0; component < 3; ++component) {
        var prediction = topLeft[component];
        for (var x = 0; x < this._stream.Width; ++x) {
          var value = pixels[rowAt + x * 3 + component];
          residuals[component][residualAt + x] = unchecked((byte)(value - prediction));
          prediction = value;
        }
        topLeft[component] = pixels[rowAt + component];
      }
    }

    var tables = new[] {
      HuffmanCodes.FromData(residuals[0]),
      HuffmanCodes.FromData(residuals[1]),
      HuffmanCodes.FromData(residuals[2]),
    };
    var bits = new WordSwappedBitWriter();
    bits.WriteBits((uint)CodingMode.Rgb24, 8);
    bits.WriteBits(0, 8);
    foreach (var table in tables)
      table.WriteDescription(bits);

    for (var y = 0; y < this._stream.Height; ++y) {
      var residualAt = y * this._stream.Width;
      for (var component = 0; component < 3; ++component)
        for (var x = 0; x < this._stream.Width; ++x)
          tables[component].Write(bits, residuals[component][residualAt + x]);
    }

    return bits.ToPacket();
  }

  private byte[] _EncodeArgb(RawImage frame) {
    var picture = LosslessEncoderInput.Prepare(
      frame, PixelFormat.Argb32, this._stream.Width, this._stream.Height, CodecName);
    var pixels = picture.PixelData.AsSpan();
    var pixelCount = checked(this._stream.Width * this._stream.Height);
    var residuals = new[] {
      new byte[pixelCount], new byte[pixelCount], new byte[pixelCount], new byte[pixelCount],
    };
    Span<int> topLeft = stackalloc int[] { 0, 128, 128, 128 };
    Span<ulong> alphaStatistics = stackalloc ulong[256];
    Span<ulong> redStatistics = stackalloc ulong[256];
    Span<ulong> greenStatistics = stackalloc ulong[256];
    Span<ulong> blueStatistics = stackalloc ulong[256];
    var statistics = new[] {
      alphaStatistics.ToArray(), redStatistics.ToArray(), greenStatistics.ToArray(), blueStatistics.ToArray(),
    };

    for (var y = 0; y < this._stream.Height; ++y) {
      var rowAt = y * this._stream.Width * 4;
      Span<int> prediction = stackalloc int[4];
      topLeft.CopyTo(prediction);

      for (var x = 0; x < this._stream.Width; ++x) {
        var pixel = y * this._stream.Width + x;
        var at = rowAt + x * 4;
        var alpha = pixels[at];
        var alphaResidual = unchecked((byte)(alpha - prediction[0]));
        residuals[0][pixel] = alphaResidual;
        ++statistics[0][alphaResidual];
        prediction[0] = alpha;

        if (alpha == 0) {
          if ((pixels[at + 1] | pixels[at + 2] | pixels[at + 3]) != 0)
            throw new NotSupportedException(
              "CLLC ARGB omits RGB symbols when alpha is zero, so hidden non-zero RGB values cannot be preserved losslessly.");
          continue;
        }

        for (var component = 1; component < 4; ++component) {
          var value = pixels[at + component];
          var residual = unchecked((byte)(value - prediction[component]));
          residuals[component][pixel] = residual;
          ++statistics[component][residual];
          prediction[component] = value;
        }
      }

      topLeft[0] = pixels[rowAt];
      if (topLeft[0] != 0) {
        topLeft[1] = pixels[rowAt + 1];
        topLeft[2] = pixels[rowAt + 2];
        topLeft[3] = pixels[rowAt + 3];
      }
    }

    var tables = new[] {
      HuffmanCodes.FromStatistics(statistics[0]),
      HuffmanCodes.FromStatistics(statistics[1]),
      HuffmanCodes.FromStatistics(statistics[2]),
      HuffmanCodes.FromStatistics(statistics[3]),
    };
    var bits = new WordSwappedBitWriter();
    bits.WriteBits((uint)CodingMode.Argb32, 8);
    bits.WriteBits(0, 8);
    foreach (var table in tables)
      table.WriteDescription(bits);

    for (var pixel = 0; pixel < pixelCount; ++pixel) {
      tables[0].Write(bits, residuals[0][pixel]);
      if (pixels[pixel * 4] == 0)
        continue;
      tables[1].Write(bits, residuals[1][pixel]);
      tables[2].Write(bits, residuals[2][pixel]);
      tables[3].Write(bits, residuals[3][pixel]);
    }

    return bits.ToPacket();
  }

  private byte[] _EncodeYuv422(RawImage frame) {
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"{CodecName} geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    if (frame.Format != PixelFormat.Yuv422P8)
      throw new NotSupportedException(
        $"CLLC YUV mode is lossless only from native {PixelFormat.Yuv422P8}; {frame.Format} would require a colour conversion or resampling.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var yPlane = frame.GetPlaneData(0);
    var uPlane = frame.GetPlaneData(1);
    var vPlane = frame.GetPlaneData(2);
    var chromaWidth = this._stream.Width / 2;
    var yResiduals = new byte[yPlane.Length];
    var uResiduals = new byte[uPlane.Length];
    var vResiduals = new byte[vPlane.Length];
    var yTopLeft = 128;
    var uTopLeft = 128;
    var vTopLeft = 128;

    for (var row = 0; row < this._stream.Height; ++row) {
      yTopLeft = _MakeResidualLine(
        yPlane.Slice(row * this._stream.Width, this._stream.Width),
        yResiduals.AsSpan(row * this._stream.Width, this._stream.Width),
        yTopLeft);
      uTopLeft = _MakeResidualLine(
        uPlane.Slice(row * chromaWidth, chromaWidth),
        uResiduals.AsSpan(row * chromaWidth, chromaWidth),
        uTopLeft);
      vTopLeft = _MakeResidualLine(
        vPlane.Slice(row * chromaWidth, chromaWidth),
        vResiduals.AsSpan(row * chromaWidth, chromaWidth),
        vTopLeft);
    }

    Span<ulong> lumaStatistics = stackalloc ulong[256];
    Span<ulong> chromaStatistics = stackalloc ulong[256];
    _Count(yResiduals, lumaStatistics);
    _Count(uResiduals, chromaStatistics);
    _Count(vResiduals, chromaStatistics);
    var lumaTable = HuffmanCodes.FromStatistics(lumaStatistics);
    var chromaTable = HuffmanCodes.FromStatistics(chromaStatistics);

    var bits = new WordSwappedBitWriter();
    bits.WriteBits((uint)CodingMode.Yuv422, 8);
    bits.WriteBits(0, 8); // non-blocked YUV, the form implemented by the reference decoder
    lumaTable.WriteDescription(bits);
    chromaTable.WriteDescription(bits);

    for (var row = 0; row < this._stream.Height; ++row) {
      var yAt = row * this._stream.Width;
      for (var x = 0; x < this._stream.Width; ++x)
        lumaTable.Write(bits, yResiduals[yAt + x]);

      var chromaAt = row * chromaWidth;
      for (var x = 0; x < chromaWidth; ++x)
        chromaTable.Write(bits, uResiduals[chromaAt + x]);
      for (var x = 0; x < chromaWidth; ++x)
        chromaTable.Write(bits, vResiduals[chromaAt + x]);
    }

    return bits.ToPacket();
  }

  private static int _MakeResidualLine(ReadOnlySpan<byte> source, Span<byte> destination, int initialPrediction) {
    var prediction = initialPrediction;
    for (var i = 0; i < source.Length; ++i) {
      var value = source[i];
      destination[i] = unchecked((byte)(value - prediction));
      prediction = value;
    }
    return source[0];
  }

  private static void _Count(ReadOnlySpan<byte> data, Span<ulong> statistics) {
    foreach (var symbol in data)
      ++statistics[symbol];
  }

  private enum CodingMode : byte {
    Yuv422 = 0,
    Rgb24 = 1,
    Argb32 = 3,
  }

  private sealed class HuffmanCodes {
    private readonly byte[] _lengths;
    private readonly uint[] _codes;
    private readonly int _maximumLength;

    private HuffmanCodes(byte[] lengths, uint[] codes, int maximumLength) {
      this._lengths = lengths;
      this._codes = codes;
      this._maximumLength = maximumLength;
    }

    internal static HuffmanCodes FromData(ReadOnlySpan<byte> data) {
      Span<ulong> statistics = stackalloc ulong[256];
      _Count(data, statistics);
      return FromStatistics(statistics);
    }

    internal static HuffmanCodes FromStatistics(ReadOnlySpan<ulong> statistics) {
      if (statistics.Length != 256)
        throw new ArgumentException("A CLLC Huffman table has exactly 256 possible byte symbols.", nameof(statistics));

      var lengths = _BuildLengths(statistics);
      var maximumLength = 0;
      for (var symbol = 0; symbol < lengths.Length; ++symbol)
        maximumLength = Math.Max(maximumLength, lengths[symbol]);

      if (maximumLength > _MAX_CODE_LENGTH) {
        Array.Fill(lengths, (byte)8);
        maximumLength = 8;
      }

      Span<int> counts = stackalloc int[_MAX_CODE_LENGTH + 1];
      foreach (var length in lengths)
        if (length != 0)
          ++counts[length];

      Span<uint> next = stackalloc uint[_MAX_CODE_LENGTH + 1];
      uint code = 0;
      for (var length = 1; length <= maximumLength; ++length) {
        code = (code + (uint)counts[length - 1]) << 1;
        next[length] = code;
      }

      var codes = new uint[256];
      for (var length = 1; length <= maximumLength; ++length)
        for (var symbol = 0; symbol < 256; ++symbol)
          if (lengths[symbol] == length)
            codes[symbol] = next[length]++;

      return new(lengths, codes, maximumLength);
    }

    internal void WriteDescription(WordSwappedBitWriter bits) {
      bits.WriteBits((uint)this._maximumLength, 5);
      for (var length = 1; length <= this._maximumLength; ++length) {
        var count = 0;
        for (var symbol = 0; symbol < 256; ++symbol)
          if (this._lengths[symbol] == length)
            ++count;

        bits.WriteBits((uint)count, 9);
        for (var symbol = 0; symbol < 256; ++symbol)
          if (this._lengths[symbol] == length)
            bits.WriteBits((uint)symbol, 8);
      }
    }

    internal void Write(WordSwappedBitWriter bits, byte symbol) {
      var length = this._lengths[symbol];
      if (length == 0)
        throw new InvalidOperationException($"CLLC symbol {symbol} has no Huffman code.");
      bits.WriteBits(this._codes[symbol], length);
    }

    private static byte[] _BuildLengths(ReadOnlySpan<ulong> statistics) {
      var left = new int[511];
      var right = new int[511];
      var weight = new ulong[511];
      var firstSymbol = new int[511];
      Array.Fill(left, -1);
      Array.Fill(right, -1);
      Array.Fill(firstSymbol, int.MaxValue);

      var queue = new PriorityQueue<int, (ulong Weight, int FirstSymbol, int Serial)>();
      var serial = 0;
      var leaves = 0;
      for (var symbol = 0; symbol < 256; ++symbol) {
        if (statistics[symbol] == 0)
          continue;

        weight[symbol] = statistics[symbol];
        firstSymbol[symbol] = symbol;
        queue.Enqueue(symbol, (weight[symbol], symbol, serial++));
        ++leaves;
      }

      if (leaves == 0) {
        weight[0] = weight[1] = 1;
        firstSymbol[0] = 0;
        firstSymbol[1] = 1;
        queue.Enqueue(0, (1, 0, serial++));
        queue.Enqueue(1, (1, 1, serial++));
      } else if (leaves == 1) {
        var only = queue.Peek();
        var dummy = only == 0 ? 1 : 0;
        weight[dummy] = 1;
        firstSymbol[dummy] = dummy;
        queue.Enqueue(dummy, (1, dummy, serial++));
      }

      var nextNode = 256;
      while (queue.Count > 1) {
        var first = queue.Dequeue();
        var second = queue.Dequeue();
        var node = nextNode++;
        left[node] = first;
        right[node] = second;
        weight[node] = weight[first] + weight[second];
        firstSymbol[node] = Math.Min(firstSymbol[first], firstSymbol[second]);
        queue.Enqueue(node, (weight[node], firstSymbol[node], serial++));
      }

      var root = queue.Dequeue();
      var lengths = new byte[256];
      var stack = new Stack<(int Node, int Depth)>();
      stack.Push((root, 0));
      while (stack.Count != 0) {
        var (node, depth) = stack.Pop();
        if (node < 256) {
          lengths[node] = checked((byte)depth);
          continue;
        }

        stack.Push((right[node], depth + 1));
        stack.Push((left[node], depth + 1));
      }

      return lengths;
    }
  }

  private sealed class WordSwappedBitWriter {
    private readonly List<byte> _bytes = [];
    private int _position;

    internal void WriteBits(uint value, int count) {
      if (count is < 0 or > 32)
        throw new ArgumentOutOfRangeException(nameof(count));

      for (var bit = count - 1; bit >= 0; --bit) {
        var byteIndex = this._position >> 3;
        if (byteIndex == this._bytes.Count)
          this._bytes.Add(0);
        if (((value >> bit) & 1) != 0)
          this._bytes[byteIndex] |= (byte)(1 << (7 - (this._position & 7)));
        ++this._position;
      }
    }

    internal byte[] ToPacket() {
      if ((this._bytes.Count & 1) != 0)
        this._bytes.Add(0);

      var result = this._bytes.ToArray();
      for (var i = 0; i < result.Length; i += 2)
        (result[i], result[i + 1]) = (result[i + 1], result[i]);
      return result;
    }
  }
}
