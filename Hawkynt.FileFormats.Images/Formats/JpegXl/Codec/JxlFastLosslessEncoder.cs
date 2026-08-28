using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Standards-conformant JPEG XL lossless modular encoder for 8-bit Gray, Gray+Alpha, RGB and RGBA.
/// </summary>
/// <remarks>
/// This is a managed C# adaptation of the fast-lossless encoder in zune-jpegxl, whose source is
/// available under MIT, Apache-2.0 or zlib terms. The port keeps the wire grammar (modular tree,
/// prefix histograms, RCT, predictor, LZ77 runs, frame header and TOC) while fitting this project's
/// existing JPEG XL bit writer and RawImage-oriented API. See THIRD_PARTY_NOTICES.md.
/// </remarks>
internal static class JxlFastLosslessEncoder {

  private const int _NUM_RAW_SYMBOLS = 19;
  private const int _NUM_LZ77 = 33;
  private const int _LZ77_MIN_LENGTH = 7;
  private const int _LZ77_CACHE_SIZE = 32;
  private const int _CHUNK_SIZE = 8;
  private const int _GROUP_SIZE = 256;
  private const int _DC_GROUP_SIZE = 2048;

  private static readonly ulong[] _BaseRawCounts = [
    3843, 852, 1270, 1214, 1014, 727, 481, 300, 159, 51, 5, 1, 1, 1, 1, 1, 1, 1, 1,
  ];

  private static readonly ulong[] _BaseLz77Counts = [
    29, 27, 25, 23, 21, 21, 19, 18, 21, 17, 16, 15, 15, 14, 13, 13, 137, 98, 61, 34,
    1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0,
  ];

  private static readonly byte[] _RawMinLengths = new byte[20];
  private static readonly byte[] _RawMaxLengths = [
    7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 10, 255, 255, 255, 255, 255, 255, 255, 255,
  ];

  internal static byte[] Encode(ReadOnlySpan<byte> pixels, int width, int height, int channels) {
    if (width < 1 || height < 1)
      throw new ArgumentOutOfRangeException(nameof(width), "JPEG XL dimensions must be positive.");
    if (channels is < 1 or > 4)
      throw new ArgumentOutOfRangeException(nameof(channels), "Fast-lossless JPEG XL supports 1..4 8-bit channels.");

    var expected = checked(width * height * channels);
    if (pixels.Length != expected)
      throw new InvalidDataException($"JPEG XL input is {pixels.Length} bytes; {width}x{height}x{channels} requires {expected}.");

    var source = pixels.ToArray();
    var numGroupsX = (width + _GROUP_SIZE - 1) / _GROUP_SIZE;
    var numGroupsY = (height + _GROUP_SIZE - 1) / _GROUP_SIZE;
    var numDcGroupsX = (width + _DC_GROUP_SIZE - 1) / _DC_GROUP_SIZE;
    var numDcGroupsY = (height + _DC_GROUP_SIZE - 1) / _DC_GROUP_SIZE;
    var oneGroup = numGroupsX == 1 && numGroupsY == 1;
    var groupCount = oneGroup ? 1 : checked(2 + numDcGroupsX * numDcGroupsY + numGroupsX * numGroupsY);

    var rawCounts = new ulong[4][];
    var lz77Counts = new ulong[4][];
    for (var c = 0; c < 4; ++c) {
      rawCounts[c] = new ulong[_NUM_RAW_SYMBOLS];
      lz77Counts[c] = new ulong[_NUM_LZ77];
    }

    // Collect over all samples rather than zune's effort-dependent sampling. Histogram selection only
    // affects compression ratio; using the complete image is deterministic and leaves the grammar unchanged.
    for (var gy = 0; gy < numGroupsY; ++gy)
    for (var gx = 0; gx < numGroupsX; ++gx) {
      var x0 = gx * _GROUP_SIZE;
      var y0 = gy * _GROUP_SIZE;
      var xs = Math.Min(_GROUP_SIZE, width - x0);
      var ys = Math.Min(_GROUP_SIZE, height - y0);
      var sinks = new IChunkSink[channels];
      for (var c = 0; c < channels; ++c)
        sinks[c] = new CountingSink(rawCounts[c], lz77Counts[c]);
      _ProcessArea(source, width, channels, x0, y0, xs, ys, sinks);
    }

    var doingYCoCg = channels > 2;
    var rawSymbolCount = doingYCoCg ? 11 : 10;
    for (var c = 0; c < 4; ++c) {
      for (var i = 0; i < _NUM_RAW_SYMBOLS; ++i) {
        var baseline = i < rawSymbolCount ? _BaseRawCounts[i] : 0UL;
        rawCounts[c][i] = checked((rawCounts[c][i] << 8) + baseline);
      }
      for (var i = 0; i < _NUM_LZ77; ++i)
        lz77Counts[c][i] = checked((lz77Counts[c][i] << 8) + _BaseLz77Counts[i]);
    }

    var codes = new PrefixCode[4];
    for (var c = 0; c < 4; ++c)
      codes[c] = PrefixCode.Create(rawCounts[c], lz77Counts[c]);

    var groups = new byte[groupCount][];
    for (var i = 0; i < groups.Length; ++i)
      groups[i] = [];

    // Global modular state lives in group zero. For a single-group frame the actual samples follow it.
    {
      var writer = new JxlBitWriter();
      _WriteDcGlobal(writer, oneGroup, channels, codes);
      if (oneGroup)
        _WritePixelGroup(writer, source, width, height, channels, 0, 0, width, height, codes, includeGroupHeader: false);
      writer.ZeroPadToByte();
      groups[0] = writer.ToArray();
    }

    if (!oneGroup) {
      for (var gy = 0; gy < numGroupsY; ++gy)
      for (var gx = 0; gx < numGroupsX; ++gx) {
        var x0 = gx * _GROUP_SIZE;
        var y0 = gy * _GROUP_SIZE;
        var xs = Math.Min(_GROUP_SIZE, width - x0);
        var ys = Math.Min(_GROUP_SIZE, height - y0);
        var id = checked(2 + numDcGroupsX * numDcGroupsY + gy * numGroupsX + gx);
        var writer = new JxlBitWriter();
        _WritePixelGroup(writer, source, width, height, channels, x0, y0, xs, ys, codes, includeGroupHeader: true);
        writer.ZeroPadToByte();
        groups[id] = writer.ToArray();
      }
    }

    var header = _WriteHeader(width, height, channels, groups);
    var total = header.Length;
    foreach (var group in groups)
      total = checked(total + group.Length);

    var result = new byte[total];
    var at = 0;
    header.CopyTo(result, at);
    at += header.Length;
    foreach (var group in groups) {
      group.CopyTo(result, at);
      at += group.Length;
    }
    return result;
  }

  private static void _WritePixelGroup(
    JxlBitWriter writer,
    byte[] pixels,
    int imageWidth,
    int imageHeight,
    int channels,
    int x0,
    int y0,
    int xs,
    int ys,
    PrefixCode[] codes,
    bool includeGroupHeader
  ) {
    if (includeGroupHeader) {
      writer.WriteBits(1, 1);
      writer.WriteBits(1, 1);
      writer.WriteBits(0, 2);
    }

    var sinks = new IChunkSink[channels];
    for (var c = 0; c < channels; ++c)
      sinks[c] = new EncodingSink(writer, codes[c]);
    _ProcessArea(pixels, imageWidth, channels, x0, y0, xs, ys, sinks);
  }

  private static void _ProcessArea(
    byte[] pixels,
    int imageWidth,
    int channels,
    int x0,
    int y0,
    int xs,
    int ys,
    IChunkSink[] sinks
  ) {
    var previous = new int[channels][];
    for (var c = 0; c < channels; ++c)
      previous[c] = new int[xs];

    for (var y = 0; y < ys; ++y) {
      var current = new int[channels][];
      for (var c = 0; c < channels; ++c)
        current[c] = new int[xs];

      _TransformRow(pixels, imageWidth, channels, x0, y0 + y, xs, current);

      for (var c = 0; c < channels; ++c) {
        var row = current[c];
        var topRow = previous[c];
        for (var chunkX = 0; chunkX < xs; chunkX += _CHUNK_SIZE) {
          var n = Math.Min(_CHUNK_SIZE, xs - chunkX);
          Span<uint> residuals = stackalloc uint[_CHUNK_SIZE];
          for (var k = 0; k < n; ++k) {
            var x = chunkX + k;
            int left, top, topLeft;
            if (y == 0) {
              left = x == 0 ? 0 : row[x - 1];
              top = left;
              topLeft = left;
            } else {
              left = x == 0 ? topRow[0] : row[x - 1];
              top = topRow[x];
              topLeft = x == 0 ? topRow[0] : topRow[x - 1];
            }

            var ac = left - topLeft;
            var ab = left - top;
            var bc = top - topLeft;
            var gradient = ac + top;
            var d = ab ^ bc;
            var s = ac ^ bc;
            var clamp = d < 0 ? top : left;
            var prediction = s < 0 ? gradient : clamp;
            residuals[k] = _PackSigned(row[x] - prediction);
          }
          sinks[c].Chunk(residuals[..n]);
        }
      }

      previous = current;
    }

    foreach (var sink in sinks)
      sink.FinalizeRun();
  }

  private static void _TransformRow(
    byte[] pixels,
    int imageWidth,
    int channels,
    int x0,
    int y,
    int xs,
    int[][] target
  ) {
    var at = checked((y * imageWidth + x0) * channels);
    for (var x = 0; x < xs; ++x) {
      switch (channels) {
        case 1:
          target[0][x] = pixels[at];
          break;
        case 2:
          target[0][x] = pixels[at];
          target[1][x] = pixels[at + 1];
          break;
        case 3:
        case 4: {
          var r = pixels[at];
          var g = pixels[at + 1];
          var b = pixels[at + 2];
          var co = r - b;
          var tmp = b + (co >> 1);
          var cg = g - tmp;
          var yy = tmp + (cg >> 1);
          target[0][x] = yy;
          target[1][x] = co;
          target[2][x] = cg;
          if (channels == 4)
            target[3][x] = pixels[at + 3];
          break;
        }
      }
      at += channels;
    }
  }

  private static uint _PackSigned(int value)
    => unchecked(((uint)value << 1) ^ (uint)(value >> 31));

  private static void _EncodeHybrid000(uint value, out int token, out int extraBits, out uint extra) {
    if (value == 0) {
      token = 0;
      extraBits = 0;
      extra = 0;
      return;
    }
    var n = 31 - System.Numerics.BitOperations.LeadingZeroCount(value | 1);
    token = n + 1;
    extraBits = n;
    extra = value - (1u << n);
  }

  private static void _EncodeHybridLz77(uint value, out int token, out int extraBits, out uint extra) {
    var n = 31 - System.Numerics.BitOperations.LeadingZeroCount(value | 1);
    if (value < 16) {
      token = (int)value;
      extraBits = 0;
      extra = 0;
      return;
    }
    token = 16 + n - 4;
    extraBits = n;
    extra = value - (1u << n);
  }

  private interface IChunkSink {
    void Chunk(ReadOnlySpan<uint> residuals);
    void FinalizeRun();
  }

  private abstract class ChunkSinkBase : IChunkSink {
    private int _run;

    public void Chunk(ReadOnlySpan<uint> residuals) {
      var prefix = 0;
      while (prefix < residuals.Length && residuals[prefix] == 0)
        ++prefix;

      if (prefix == residuals.Length && (this._run > 0 || prefix > _LZ77_MIN_LENGTH)) {
        this._run += prefix;
        return;
      }

      if (prefix + this._run > _LZ77_MIN_LENGTH) {
        this.EmitRun(this._run + prefix);
        this.EmitResiduals(residuals[prefix..]);
        this._run = 0;
        return;
      }

      this.EmitResiduals(residuals);
    }

    public virtual void FinalizeRun() {
      if (this._run > 0)
        this.EmitRun(this._run);
      this._run = 0;
    }

    protected abstract void EmitRun(int count);
    protected abstract void EmitResiduals(ReadOnlySpan<uint> residuals);
  }

  private sealed class CountingSink(ulong[] rawCounts, ulong[] lz77Counts) : ChunkSinkBase {
    protected override void EmitRun(int count) {
      if (count == 0)
        return;
      ++rawCounts[0];
      var adjusted = checked((uint)(count - (_LZ77_MIN_LENGTH + 1)));
      _EncodeHybridLz77(adjusted, out var token, out _, out _);
      ++lz77Counts[token];
    }

    protected override void EmitResiduals(ReadOnlySpan<uint> residuals) {
      foreach (var value in residuals) {
        _EncodeHybrid000(value, out var token, out _, out _);
        ++rawCounts[token];
      }
    }

    // zune deliberately does not charge a final pending zero run to the histogram pass.
    public override void FinalizeRun() { }
  }

  private sealed class EncodingSink(JxlBitWriter writer, PrefixCode code) : ChunkSinkBase {
    protected override void EmitRun(int count) {
      if (count == 0)
        return;

      _WriteCode(writer, code.RawBits[0], code.RawNBits[0]);
      var adjusted = checked((uint)(count - (_LZ77_MIN_LENGTH + 1)));
      _EncodeHybridLz77(adjusted, out var token, out var extraBits, out var extra);
      _WriteCodeWithExtra(writer, code.Lz77Bits[token], code.Lz77NBits[token], extra, extraBits);
    }

    protected override void EmitResiduals(ReadOnlySpan<uint> residuals) {
      foreach (var value in residuals) {
        _EncodeHybrid000(value, out var token, out var extraBits, out var extra);
        _WriteCodeWithExtra(writer, code.RawBits[token], code.RawNBits[token], extra, extraBits);
      }
    }
  }

  private static void _WriteCode(JxlBitWriter writer, ushort bits, byte nbits) {
    if (nbits == 0)
      throw new InvalidDataException("JPEG XL prefix code references an absent symbol.");
    writer.WriteBits(bits, nbits);
  }

  private static void _WriteCodeWithExtra(JxlBitWriter writer, ushort bits, byte nbits, uint extra, int extraBits) {
    if (nbits == 0)
      throw new InvalidDataException("JPEG XL prefix code references an absent symbol.");
    var merged = (ulong)bits | ((ulong)extra << nbits);
    writer.WriteBits64(merged, nbits + extraBits);
  }

  private static void _WriteDcGlobal(JxlBitWriter output, bool oneGroup, int channels, PrefixCode[] codes) {
    output.WriteBits(1, 1); // default DC dequantization matrices
    output.WriteBits(1, 1); // global tree/histograms
    output.WriteBits(0, 1); // no LZ77 while coding the MA tree

    output.WriteBits(1, 1); // simple context map for tree
    output.WriteBits(0, 2); // one cluster
    output.WriteBits(1, 1); // prefix coding for tree
    output.WriteBits(0, 4); // HybridUint 000
    output.WriteBits(0b100011, 6); // alphabet size four, Var16 encoding
    output.WriteBits(1, 2); // simple prefix code
    output.WriteBits(3, 2); // four symbols
    output.WriteBits(0, 2);
    output.WriteBits(1, 2);
    output.WriteBits(2, 2);
    output.WriteBits(3, 2);
    output.WriteBits(0, 1); // first tree encoding option

    ReadOnlySpan<byte> indices = [1, 2, 1, 4, 1, 0, 0, 5, 0, 0, 0, 0, 5, 0, 0, 0, 0, 5, 0, 0, 0, 0, 5, 0, 0, 0];
    ReadOnlySpan<byte> symbolBits = [0b00, 0b10, 0b001, 0b101, 0b0011, 0b0111];
    ReadOnlySpan<byte> symbolNBits = [2, 2, 3, 3, 4, 4];
    foreach (var index in indices)
      output.WriteBits(symbolBits[index], symbolNBits[index]);

    output.WriteBits(1, 1); // LZ77 for pixel symbols
    output.WriteBits(0, 2); // LZ77 offset 224
    output.WriteBits(0b1010, 4); // minimum match length seven
    output.WriteBits(4, 4); // 400 HybridUint config
    output.WriteBits(0, 3);
    output.WriteBits(0, 3);

    output.WriteBits(1, 1); // simple context map
    output.WriteBits(3, 2); // three bits per map entry
    output.WriteBits(4, 3);
    output.WriteBits(3, 3);
    output.WriteBits(2, 3);
    output.WriteBits(1, 3);
    output.WriteBits(0, 3);

    output.WriteBits(1, 1); // prefix entropy coding
    output.WriteBits(0, 4); // distance HybridUint 000
    for (var i = 0; i < 4; ++i)
      output.WriteBits(0, 4); // pixel HybridUint 000
    output.WriteBits(1, 5); // distance alphabet size two
    for (var i = 0; i < 4; ++i) {
      output.WriteBits(1, 1);
      output.WriteBits(8, 4);
      output.WriteBits(256, 8); // symbol+LZ77 alphabet size 512
    }

    output.WriteBits(1, 2); // simple distance prefix code
    output.WriteBits(0, 2); // one symbol
    output.WriteBits(1, 1); // distance one

    foreach (var code in codes)
      code.WriteTo(output);

    output.WriteBits(1, 1); // modular group header
    output.WriteBits(1, 1);

    if (channels > 2) {
      output.WriteBits(1, 2); // one transform
      output.WriteBits(0, 2); // reversible colour transform
      output.WriteBits(0, 5); // starts at channel zero
      output.WriteBits(0, 2); // YCoCg permutation
    } else
      output.WriteBits(0, 2); // no transforms

    if (!oneGroup)
      output.ZeroPadToByte();
  }

  private static byte[] _WriteHeader(int width, int height, int channels, IReadOnlyList<byte[]> groups) {
    var output = new JxlBitWriter();

    output.WriteBits(0x0AFF, 16); // codestream signature -> FF 0A with LSB-first writer
    output.WriteBits(0, 1); // non-small size header
    _WriteDimension(output, height);
    output.WriteBits(0, 3); // no aspect-ratio shortcut
    _WriteDimension(output, width);

    // ImageMetadata: deliberately explicit, matching zune-jpegxl's fast-lossless profile.
    output.WriteBits(0, 1); // all_default = false
    output.WriteBits(0, 1); // no extra metadata fields
    output.WriteBits(0, 1); // integer samples
    output.WriteBits(0, 2); // eight bits/sample
    output.WriteBits(1, 1); // 16-bit modular buffer is sufficient

    var hasAlpha = channels is 2 or 4;
    if (hasAlpha) {
      output.WriteBits(1, 2); // one extra channel
      output.WriteBits(1, 1); // alpha channel defaults
    } else
      output.WriteBits(0, 2); // no extra channels

    output.WriteBits(0, 1); // not XYB
    if (channels > 2)
      output.WriteBits(1, 1); // default sRGB colour encoding
    else {
      output.WriteBits(0, 1); // explicit grayscale encoding
      output.WriteBits(0, 1); // no ICC profile
      output.WriteBits(1, 2); // grayscale
      output.WriteBits(1, 2); // D65
      output.WriteBits(0, 1); // enumerated transfer function
      output.WriteBits(0b10, 2);
      output.WriteBits(11, 4); // sRGB transfer function
      output.WriteBits(1, 2); // relative rendering intent
    }
    output.WriteBits(0, 2); // metadata extensions
    output.WriteBits(1, 1); // default transform data
    output.ZeroPadToByte();

    // One regular modular frame.
    output.WriteBits(0, 1); // frame all_default = false
    output.WriteBits(0, 2); // regular frame
    output.WriteBits(1, 1); // modular encoding
    output.WriteBits(0, 2); // flags
    output.WriteBits(0, 1); // not YCbCr
    output.WriteBits(0, 2); // no upsampling
    if (hasAlpha)
      output.WriteBits(0, 2); // no alpha upsampling
    output.WriteBits(1, 2); // default group size
    output.WriteBits(0, 2); // exactly one pass
    output.WriteBits(0, 1); // no custom frame size/origin
    output.WriteBits(0, 2); // replace blend mode
    if (hasAlpha)
      output.WriteBits(0, 2); // replace alpha blend mode
    output.WriteBits(1, 1); // is_last
    output.WriteBits(0, 2); // no frame name
    output.WriteBits(0, 1); // loop filter is explicit
    output.WriteBits(0, 1); // no Gaborish
    output.WriteBits(0, 2); // zero EPF iterations
    output.WriteBits(0, 2); // LF extensions
    output.WriteBits(0, 2); // frame extensions
    output.WriteBits(0, 1); // no TOC permutation
    output.ZeroPadToByte();

    foreach (var group in groups)
      _WriteGroupSize(output, group.Length);
    output.ZeroPadToByte();
    return output.ToArray();
  }

  private static void _WriteDimension(JxlBitWriter output, int size) {
    if (size < 1 || size >= 1 << 30)
      throw new ArgumentOutOfRangeException(nameof(size));
    var value = checked((uint)(size - 1));
    if (value < 1u << 9) {
      output.WriteBits(0, 2);
      output.WriteBits(value, 9);
    } else if (value < 1u << 13) {
      output.WriteBits(1, 2);
      output.WriteBits(value, 13);
    } else if (value < 1u << 18) {
      output.WriteBits(2, 2);
      output.WriteBits(value, 18);
    } else {
      output.WriteBits(3, 2);
      output.WriteBits(value, 30);
    }
  }

  private static void _WriteGroupSize(JxlBitWriter output, int size) {
    if (size < 0)
      throw new ArgumentOutOfRangeException(nameof(size));
    var value = (uint)size;
    if (value < 1u << 10) {
      output.WriteBits(0, 2);
      output.WriteBits(value, 10);
    } else if (value - 1024u < 1u << 14) {
      output.WriteBits(1, 2);
      output.WriteBits(value - 1024u, 14);
    } else if (value - 17408u < 1u << 22) {
      output.WriteBits(2, 2);
      output.WriteBits(value - 17408u, 22);
    } else {
      output.WriteBits(3, 2);
      output.WriteBits(value - 4211712u, 30);
    }
  }

  private sealed class PrefixCode {
    internal byte[] RawNBits { get; } = new byte[_NUM_RAW_SYMBOLS];
    internal ushort[] RawBits { get; } = new ushort[_NUM_RAW_SYMBOLS];
    internal byte[] Lz77NBits { get; } = new byte[_NUM_LZ77];
    internal ushort[] Lz77Bits { get; } = new ushort[_NUM_LZ77];

    internal static PrefixCode Create(ulong[] rawCounts, ulong[] lz77Counts) {
      var result = new PrefixCode();
      var level1Counts = new ulong[_NUM_RAW_SYMBOLS + 1];
      Array.Copy(rawCounts, level1Counts, _NUM_RAW_SYMBOLS);
      var numRaw = _NUM_RAW_SYMBOLS;
      while (numRaw > 0 && level1Counts[numRaw - 1] == 0)
        --numRaw;

      level1Counts[numRaw] = 0;
      for (var i = 0; i < _NUM_LZ77; ++i)
        level1Counts[numRaw] = checked(level1Counts[numRaw] + lz77Counts[i]);

      var level1NBits = new byte[_NUM_RAW_SYMBOLS + 1];
      _ComputeCodeLengths(level1Counts, numRaw + 1, _RawMinLengths, _RawMaxLengths, level1NBits);

      var level2NBits = new byte[_NUM_LZ77];
      var minLengths = new byte[_NUM_LZ77];
      var maxLengths = new byte[_NUM_LZ77];
      var l = checked((byte)(15 - level1NBits[numRaw]));
      Array.Fill(maxLengths, l);
      var numLz77 = _NUM_LZ77;
      while (numLz77 > 0 && lz77Counts[numLz77 - 1] == 0)
        --numLz77;
      _ComputeCodeLengths(lz77Counts, numLz77, minLengths, maxLengths, level2NBits);

      Array.Copy(level1NBits, result.RawNBits, numRaw);
      for (var i = 0; i < numLz77; ++i)
        result.Lz77NBits[i] = level2NBits[i] == 0 ? (byte)0 : checked((byte)(level1NBits[numRaw] + level2NBits[i]));

      _ComputeCanonicalCode(result.RawNBits.AsSpan(0, numRaw), result.RawBits.AsSpan(0, numRaw), result.Lz77NBits, result.Lz77Bits);
      return result;
    }

    internal void WriteTo(JxlBitWriter writer) {
      var codeLengthCounts = new ulong[18];
      codeLengthCounts[17] = 3 + 2 * (_NUM_LZ77 - 1);
      foreach (var nbits in this.RawNBits)
        ++codeLengthCounts[nbits];
      foreach (var nbits in this.Lz77NBits)
        ++codeLengthCounts[nbits];

      var codeLengthNBits = new byte[18];
      _ComputeCodeLengths(codeLengthCounts, 18, new byte[18], new byte[18] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 }, codeLengthNBits);

      writer.WriteBits(0, 2); // HSKIP = 0
      ReadOnlySpan<byte> order = [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];
      ReadOnlySpan<byte> lengthNBits = [2, 4, 3, 2, 2, 4];
      ReadOnlySpan<byte> lengthBits = [0, 7, 3, 2, 1, 15];
      var count = order.Length;
      while (count > 0 && codeLengthNBits[order[count - 1]] == 0)
        --count;
      for (var i = 0; i < count; ++i) {
        var symbol = codeLengthNBits[order[i]];
        writer.WriteBits(lengthBits[symbol], lengthNBits[symbol]);
      }

      var codeLengthBits = new ushort[18];
      _ComputeCanonicalCode(ReadOnlySpan<byte>.Empty, Span<ushort>.Empty, codeLengthNBits, codeLengthBits);
      foreach (var nbits in this.RawNBits)
        writer.WriteBits(codeLengthBits[nbits], codeLengthNBits[nbits]);

      var numLz77 = _NUM_LZ77;
      while (numLz77 > 0 && this.Lz77NBits[numLz77 - 1] == 0)
        --numLz77;

      writer.WriteBits(codeLengthBits[17], codeLengthNBits[17]);
      writer.WriteBits(0b010, 3);
      writer.WriteBits(codeLengthBits[17], codeLengthNBits[17]);
      writer.WriteBits(0b000, 3);
      writer.WriteBits(codeLengthBits[17], codeLengthNBits[17]);
      writer.WriteBits(0b010, 3);

      for (var i = 0; i < numLz77; ++i) {
        var nbits = this.Lz77NBits[i];
        writer.WriteBits(codeLengthBits[nbits], codeLengthNBits[nbits]);
      }
    }
  }

  private static void _ComputeCodeLengths(
    IReadOnlyList<ulong> frequencies,
    int n,
    IReadOnlyList<byte> minLimitInput,
    IReadOnlyList<byte> maxLimitInput,
    byte[] output
  ) {
    if (n == 0)
      return;

    var compactFreqs = new List<ulong>(n);
    var compactMin = new List<byte>(n);
    var compactMax = new List<byte>(n);
    for (var i = 0; i < n; ++i) {
      if (frequencies[i] == 0)
        continue;
      compactFreqs.Add(frequencies[i]);
      compactMin.Add(minLimitInput[i]);
      compactMax.Add(maxLimitInput[i]);
    }

    var compactBits = new byte[compactFreqs.Count];
    _ComputeCodeLengthsNonZero(compactFreqs, compactMin, compactMax, compactBits);

    Array.Clear(output);
    var at = 0;
    for (var i = 0; i < n; ++i)
      if (frequencies[i] != 0)
        output[i] = compactBits[at++];
  }

  private static void _ComputeCodeLengthsNonZero(
    IReadOnlyList<ulong> frequencies,
    IList<byte> minLimit,
    IReadOnlyList<byte> maxLimit,
    byte[] output
  ) {
    if (frequencies.Count == 0)
      return;
    if (frequencies.Count == 1) {
      output[0] = Math.Max((byte)1, minLimit[0]);
      return;
    }

    var precision = 0;
    var shortest = byte.MaxValue;
    ulong sum = 0;
    for (var i = 0; i < frequencies.Count; ++i) {
      sum = checked(sum + frequencies[i]);
      if (minLimit[i] < 1)
        minLimit[i] = 1;
      if (minLimit[i] > maxLimit[i])
        throw new InvalidDataException("Impossible JPEG XL prefix-code length constraints.");
      precision = Math.Max(precision, maxLimit[i]);
      shortest = Math.Min(shortest, minLimit[i]);
    }
    precision -= shortest - 1;
    if (precision is < 1 or > 20)
      throw new InvalidDataException($"JPEG XL prefix-code precision {precision} is outside the supported range.");

    var width = checked((1 << precision) + 1);
    var infinity = checked(sum * (ulong)precision);
    var dynamic = new ulong[checked(width * (frequencies.Count + 1))];
    Array.Fill(dynamic, infinity);
    dynamic[0] = 0;

    static int Offset(int symbol, int off, int width) => checked(symbol * width + off);

    for (var symbol = 0; symbol < frequencies.Count; ++symbol) {
      for (var bits = minLimit[symbol]; bits <= maxLimit[symbol]; ++bits) {
        var delta = 1 << (precision - bits);
        for (var off = 0; off <= (1 << precision) - delta; ++off) {
          var previous = dynamic[Offset(symbol, off, width)];
          if (previous >= infinity)
            continue;
          var destination = Offset(symbol + 1, off + delta, width);
          var cost = checked(previous + frequencies[symbol] * bits);
          if (cost < dynamic[destination])
            dynamic[destination] = cost;
        }
      }
    }

    var sym = frequencies.Count;
    var position = 1 << precision;
    if (dynamic[Offset(sym, position, width)] >= infinity)
      throw new InvalidDataException("Could not construct a JPEG XL prefix code under the required length constraints.");

    while (sym > 0) {
      --sym;
      var found = false;
      for (var bits = minLimit[sym]; bits <= maxLimit[sym]; ++bits) {
        var delta = 1 << (precision - bits);
        if (delta > position)
          continue;
        var here = dynamic[Offset(sym + 1, position, width)];
        var before = dynamic[Offset(sym, position - delta, width)];
        if (before >= infinity)
          continue;
        if (here == checked(before + frequencies[sym] * bits)) {
          position -= delta;
          output[sym] = bits;
          found = true;
          break;
        }
      }
      if (!found)
        throw new InvalidDataException("Could not backtrack a JPEG XL prefix code.");
    }
  }

  private static void _ComputeCanonicalCode(
    ReadOnlySpan<byte> firstNBits,
    Span<ushort> firstBits,
    ReadOnlySpan<byte> secondNBits,
    Span<ushort> secondBits
  ) {
    const int MaxLength = 15;
    Span<int> counts = stackalloc int[MaxLength + 1];
    foreach (var nbits in firstNBits) {
      if (nbits is 0 or > MaxLength)
        throw new InvalidDataException("Invalid JPEG XL prefix-code length.");
      ++counts[nbits];
    }
    foreach (var nbits in secondNBits) {
      if (nbits > MaxLength)
        throw new InvalidDataException("Invalid JPEG XL prefix-code length.");
      ++counts[nbits]; // Keep zune's exact canonical-code construction, including absent entries.
    }

    Span<int> nextCode = stackalloc int[MaxLength + 1];
    var code = 0;
    for (var i = 1; i <= MaxLength; ++i) {
      code = (code + counts[i - 1]) << 1;
      nextCode[i] = code;
    }

    for (var i = 0; i < firstBits.Length; ++i) {
      var nbits = firstNBits[i];
      firstBits[i] = _ReverseLowBits((ushort)nextCode[nbits], nbits);
      ++nextCode[nbits];
    }
    for (var i = 0; i < secondBits.Length; ++i) {
      var nbits = secondNBits[i];
      secondBits[i] = nbits == 0 ? (ushort)0 : _ReverseLowBits((ushort)nextCode[nbits], nbits);
      if (nbits != 0)
        ++nextCode[nbits];
    }
  }

  private static ushort _ReverseLowBits(ushort value, int count) {
    uint x = value;
    x = ((x & 0x5555u) << 1) | ((x >> 1) & 0x5555u);
    x = ((x & 0x3333u) << 2) | ((x >> 2) & 0x3333u);
    x = ((x & 0x0F0Fu) << 4) | ((x >> 4) & 0x0F0Fu);
    x = (x << 8) | (x >> 8);
    return count == 0 ? (ushort)0 : (ushort)(x >> (16 - count));
  }
}
