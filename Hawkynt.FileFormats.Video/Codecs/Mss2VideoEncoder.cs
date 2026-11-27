using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes MS Screen 2 (<c>MSS2</c>) in its run-length palette mode: key frames that restate the
/// palette and code every pixel, and inter frames that code only the rectangle that changed.
/// </summary>
/// <remarks>
/// The package's own encoder — FFmpeg has none — writing the syntax of FFmpeg's <c>decode_rle</c>:
/// a per-frame Huffman code described by its explicit codes and the lexicographic rest, literal
/// palette indices, run codes, "same as the row above" and, in inter frames, "unchanged". A picture of
/// 256 colours or fewer comes back exactly; a new key frame is written whenever a picture needs a
/// colour the current palette does not have, and one with more than 256 colours is mapped to a fixed
/// 6x7x6 colour cube, which is the only loss. FFmpeg's decoder is the oracle that reads what it writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mss2VideoEncoder : IVideoCodecEncoder<Mss2VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MSS2");

  private const int _KEY_FRAME_INTERVAL = 120;
  private const int _ABOVE = 268;
  private const int _SKIP = 269;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private uint[] _palette = [];
  private Dictionary<int, int> _lookup = [];
  private byte[]? _last;
  private int _sinceKeyFrame;
  private MediaStreamInfo? _stream;

  private Mss2VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
  }

  public static string CodecName => Mss2VideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static Mss2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MS Screen 2 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0 || stream.Width > 4096 || stream.Height > 4096)
      throw new NotSupportedException($"MS Screen 2 pictures are 1 to 4096 samples a side; {stream.Width}x{stream.Height} was asked for.");
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"This MS Screen 2 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    var rgb = frame.ToRgb24();
    var key = this._last == null || this._sinceKeyFrame >= _KEY_FRAME_INTERVAL || !this._Fits(rgb);
    if (key)
      this._BuildPalette(rgb);

    // The coder's row nought is the picture's bottom row.
    var indices = new byte[this._width * this._height];
    for (var y = 0; y < this._height; ++y)
      for (var x = 0; x < this._width; ++x) {
        var at = ((this._height - 1 - y) * this._width + x) * 3;
        indices[y * this._width + x] = (byte)this._IndexOf(rgb[at] << 16 | rgb[at + 1] << 8 | rgb[at + 2]);
      }

    var writer = new MsMpeg4BitWriter();
    writer.Write(key ? 1 : 0, 1);
    if (key)
      writer.Write(0, 7);
    writer.Write(0, 1); // no WMV9 rectangles
    if (!key)
      writer.Write(0, 1); // no motion vector
    writer.Write(1, 1); // run lengths
    writer.Write(0, 1); // palette indices, not 15-bit
    _Align(writer);

    if (key) {
      writer.Write(this._palette.Length, 8);
      foreach (var colour in this._palette)
        writer.Write((int)colour, 24);
    }

    this._WriteRle(writer, indices, key);
    _Align(writer);

    this._last = indices;
    this._sinceKeyFrame = key ? 1 : this._sinceKeyFrame + 1;
    packet = new(this._requested.Index, writer.ToArray(), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, IsKeyFrame: key);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var extra = new byte[60 + 256 * 3];
    BinaryPrimitives.WriteUInt32BigEndian(extra, (uint)extra.Length);
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(4), 2); // encoder version 2: MS Screen 2
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(12), (uint)this._width);
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(16), (uint)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(20), (uint)this._width);
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(24), (uint)this._height);
    var rate = this._requested.FrameRate.Denominator > 0 ? (float)this._requested.FrameRate.Numerator / this._requested.FrameRate.Denominator : 15f;
    BinaryPrimitives.WriteSingleBigEndian(extra.AsSpan(28), rate);
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(48), 256); // every palette entry may change
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(52), 0); // no slice split
    BinaryPrimitives.WriteUInt32BigEndian(extra.AsSpan(56), 256); // colours the region coder may use

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize + extra.Length, Width: this._width, Height: this._height, Planes: 1, BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value), ImageSize: 0, XPixelsPerMeter: 0, YPixelsPerMeter: 0, ColorsUsed: 0, ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize + extra.Length];
    header.WriteTo(format);
    extra.CopyTo(format, BitmapInfoHeader.StructSize);
    return this._stream = new() {
      Index = this._requested.Index, Kind = MediaStreamKind.Video, Codec = _Tag, Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC", TimeBase = this._requested.TimeBase, FrameRate = this._requested.FrameRate,
      Width = this._width, Height = this._height, BitsPerPixel = 24, CodecPrivateData = format,
    };
  }

  // ============================================================================================
  // The palette
  // ============================================================================================

  private bool _Fits(byte[] rgb) {
    for (var i = 0; i < rgb.Length; i += 3)
      if (!this._lookup.ContainsKey(rgb[i] << 16 | rgb[i + 1] << 8 | rgb[i + 2]))
        return false;
    return true;
  }

  /// <summary>The picture's own colours where there are at most 255 of them, else a 6x7x6 cube.</summary>
  private void _BuildPalette(byte[] rgb) {
    var colours = new List<int>();
    var seen = new HashSet<int>();
    for (var i = 0; i < rgb.Length && colours.Count <= 255; i += 3) {
      var colour = rgb[i] << 16 | rgb[i + 1] << 8 | rgb[i + 2];
      if (seen.Add(colour))
        colours.Add(colour);
    }

    if (colours.Count > 255) {
      colours.Clear();
      for (var r = 0; r < 6; ++r)
        for (var g = 0; g < 7; ++g)
          for (var b = 0; b < 6; ++b)
            colours.Add(r * 51 << 16 | (g * 255 / 6) << 8 | b * 51);
    }

    this._palette = colours.ConvertAll(static c => (uint)c).ToArray();
    this._lookup = [];
    for (var i = 0; i < this._palette.Length; ++i)
      this._lookup.TryAdd((int)this._palette[i], i);
  }

  private int _IndexOf(int colour) {
    if (this._lookup.TryGetValue(colour, out var index))
      return index;

    var best = 0;
    var distance = int.MaxValue;
    for (var i = 0; i < this._palette.Length; ++i) {
      var p = (int)this._palette[i];
      int dr = (p >> 16) - (colour >> 16), dg = ((p >> 8) & 0xFF) - ((colour >> 8) & 0xFF), db = (p & 0xFF) - (colour & 0xFF);
      var d = dr * dr + dg * dg + db * db;
      if (d < distance) {
        distance = d;
        best = i;
      }
    }

    this._lookup[colour] = best;
    return best;
  }

  // ============================================================================================
  // Run lengths
  // ============================================================================================

  private void _WriteRle(MsMpeg4BitWriter writer, byte[] indices, bool key) {
    int x0 = 0, y0 = 0, x1 = this._width - 1, y1 = this._height - 1;
    if (!key) {
      // The rectangle that changed; a picture with no change still codes one unchanged pixel.
      x0 = this._width;
      y0 = this._height;
      x1 = -1;
      y1 = -1;
      for (var y = 0; y < this._height; ++y)
        for (var x = 0; x < this._width; ++x)
          if (indices[y * this._width + x] != this._last![y * this._width + x]) {
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
          }

      if (x1 < 0)
        x0 = y0 = x1 = y1 = 0;
    }

    var width = x1 - x0 + 1;
    var height = y1 - y0 + 1;
    var symbols = new List<(int Symbol, int Extra, int ExtraLength)>();
    var count = new int[270];

    void Emit(int symbol, int extra = 0, int extraLength = 0) {
      symbols.Add((symbol, extra, extraLength));
      ++count[symbol];
    }

    void EmitRepeat(int pixels) {
      // A repeat code stands for itself and n further pixels.
      while (pixels > 0) {
        var n = Math.Min(pixels - 1, (1 << 26) - 2);
        var b = 31 - int.LeadingZeroCount(n + 1);
        var value = n - ((1 << b) - 1);
        if (b <= 10)
          Emit(256 + b, value, b);
        // Code 267 states the length itself in four more bits, from ten upwards.
        else
          Emit(267, (b - 10) << b | value, 4 + b);
        pixels -= n + 1;
      }
    }

    var position = 0;
    var total = width * height;
    int At(int p) => (y0 + p / width) * this._width + x0 + p % width;
    while (position < total) {
      var row = position / width;
      var here = indices[At(position)];

      int Run(Func<int, bool> same) {
        var run = 0;
        while (position + run < total && same(position + run))
          ++run;
        return run;
      }

      var unchanged = key ? 0 : Run(p => indices[At(p)] == this._last![At(p)]);
      var above = y0 + row > 0 ? Run(p => indices[At(p)] == indices[At(p) - this._width]) : 0;
      var literal = Run(p => indices[At(p)] == here);

      if (unchanged > 0 && unchanged >= above && unchanged >= literal) {
        Emit(_SKIP);
        EmitRepeat(unchanged - 1);
        position += unchanged;
      } else if (above > literal) {
        Emit(_ABOVE);
        EmitRepeat(above - 1);
        position += above;
      } else {
        Emit(here);
        EmitRepeat(literal - 1);
        position += literal;
      }
    }

    if (!key) {
      writer.Write(x0, 12);
      writer.Write(y0, 12);
      writer.Write(width - 1, 12);
      writer.Write(height - 1, 12);
    }

    var codes = _WriteCode(writer, count, key);
    foreach (var (symbol, extra, extraLength) in symbols) {
      writer.Write(codes[symbol].Code, codes[symbol].Length);
      if (extraLength != 0)
        writer.Write(extra, extraLength);
    }
  }

  /// <summary>
  /// Writes the frame's Huffman code as <c>decode_rle</c> reads it — the used symbols explicitly,
  /// length by length, then the unused ones left to be filled in lexicographically — and returns it.
  /// </summary>
  private static (int Code, int Length)[] _WriteCode(MsMpeg4BitWriter writer, int[] count, bool key) {
    var alphabet = key ? 269 : 270;
    var used = new List<int>();
    for (var s = 0; s < alphabet; ++s)
      if (count[s] > 0)
        used.Add(s);

    // Huffman lengths over the used symbols and one more for the unused ones, so that room is left
    // for them, capped at 20 bits by flattening the rarest.
    var lengths = _HuffmanLengths(used.ConvertAll(s => count[s]), 20);
    var ordered = new List<(int Length, int Symbol)>();
    for (var i = 0; i < used.Count; ++i)
      ordered.Add((lengths[i], used[i]));
    ordered.Sort();

    var result = new (int, int)[270];
    var currentLength = 0;
    var nextCode = 0;
    var index = 0;
    while (true) {
      ++currentLength;
      nextCode <<= 1;
      var remaining = (1 << currentLength) - nextCode;
      var atLength = 0;
      while (index + atLength < ordered.Count && ordered[index + atLength].Length == currentLength)
        ++atLength;
      var stop = index == ordered.Count;
      var stated = stop ? remaining : atLength;
      writer.Write(stated, _CeilLog2(remaining + 1));
      if (stop)
        break;

      for (var i = 0; i < atLength; ++i) {
        var symbol = ordered[index + i].Symbol;
        _WriteSymbol(writer, symbol, key);
        result[symbol] = (nextCode++, currentLength);
      }

      index += atLength;
    }

    // The rest of the alphabet, as the decoder assigns it.
    var left = alphabet - used.Count;
    int surplus;
    while ((surplus = (2 << currentLength) - (nextCode << 1) - left) < 0) {
      ++currentLength;
      nextCode <<= 1;
    }

    for (var s = 0; s < alphabet; ++s)
      if (count[s] == 0) {
        if (surplus-- == 0) {
          ++currentLength;
          nextCode <<= 1;
        }

        result[s] = (nextCode++, currentLength);
      }

    if (nextCode != 1 << currentLength)
      throw new InvalidOperationException("The MS Screen 2 run-length code does not fill its space.");
    return result;
  }

  /// <summary>The eight-bit form a symbol is listed in, with a ninth bit for the ones that share a code.</summary>
  private static void _WriteSymbol(MsMpeg4BitWriter writer, int symbol, bool key) {
    var k = key ? 1 : 0;
    if (symbol < 190) {
      writer.Write(symbol, 8);
    } else if (symbol < 2 * (204 - k) - 190) {
      writer.Write((symbol + 190) >> 1, 8);
      writer.Write((symbol + 190) & 1, 1);
    } else {
      writer.Write(symbol - (14 - k), 8);
    }
  }

  /// <summary>
  /// Huffman code lengths for the weights given plus one extra weight of one — the room left for the
  /// symbols the frame does not use — with the weights halved until no code is longer than
  /// <paramref name="limit"/>.
  /// </summary>
  private static int[] _HuffmanLengths(List<int> weights, int limit) {
    var current = new List<long>(weights.ConvertAll(static w => (long)w)) { 1 };
    while (true) {
      var lengths = _Lengths(current);
      var longest = 0;
      foreach (var length in lengths)
        longest = Math.Max(longest, length);
      if (longest <= limit)
        return lengths[..weights.Count];
      for (var i = 0; i < current.Count; ++i)
        current[i] = (current[i] + 1) >> 1;
    }
  }

  private static int[] _Lengths(List<long> weights) {
    var n = weights.Count;
    var parent = new List<int>();
    var queue = new PriorityQueue<int, (long, int)>();
    for (var i = 0; i < n; ++i) {
      parent.Add(-1);
      queue.Enqueue(i, (weights[i], i));
    }

    var nodeWeight = new List<long>(weights);
    while (queue.Count > 1) {
      queue.TryDequeue(out var a, out var pa);
      queue.TryDequeue(out var b, out var pb);
      var node = parent.Count;
      parent.Add(-1);
      nodeWeight.Add(pa.Item1 + pb.Item1);
      parent[a] = node;
      parent[b] = node;
      queue.Enqueue(node, (pa.Item1 + pb.Item1, node));
    }

    var lengths = new int[n];
    for (var i = 0; i < n; ++i) {
      var depth = 0;
      for (var p = parent[i]; p >= 0; p = parent[p])
        ++depth;
      lengths[i] = Math.Max(depth, 1);
    }

    return lengths;
  }

  private static int _CeilLog2(int v) => v <= 1 ? 0 : 32 - int.LeadingZeroCount(v - 1);

  private static void _Align(MsMpeg4BitWriter writer) {
    while (writer.BitCount % 8 != 0)
      writer.WriteBit(0);
  }
}
