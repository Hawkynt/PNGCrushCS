using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Gif;

/// <summary>GIF LZW encoder + decoder, including the 1..255-byte sub-block framing the spec wraps the
/// bitstream in.</summary>
/// <remarks>
/// <para><b>Encoder.</b> Variable-width LZW with codes growing 3..12 bits, clear-code emitted on
/// dictionary overflow. The "deferred clear code" optimisation (don't clear immediately when full —
/// keep using the existing dictionary as a static codebook for a few iterations) is supported via the
/// <see cref="EncodeOptions.DeferClear"/> flag; readers don't notice the difference.</para>
/// <para><b>Decoder.</b> Standard back-reference walk with a 4096-entry table. Tolerates the legacy
/// "code = next-allocated-index" first-character special case (the so-called KwKwK pattern).</para>
/// </remarks>
internal static class GifLzwCodec {

  private const int _MaxCodeBits = 12;
  private const int _MaxCodes = 1 << _MaxCodeBits;

  /// <summary>Compression strategy. <see cref="CompressionLevel.None"/> emits each pixel as a literal code
  /// (largest output, useful as a baseline for testing or for downstream re-compression).
  /// <see cref="CompressionLevel.Standard"/> is the default LZW path with periodic clears.
  /// <see cref="CompressionLevel.Best"/> runs every reasonable Standard variant and keeps the smallest —
  /// the GIF analogue of Zopfli's exhaustive deflate parameter search.</summary>
  public enum CompressionLevel {
    None,
    Standard,
    Best,
  }

  /// <summary>Encoder configuration. <see cref="DeferClear"/> only affects <see cref="CompressionLevel.Standard"/>;
  /// <see cref="CompressionLevel.Best"/> overrides it by trying both values internally.</summary>
  public sealed record EncodeOptions(CompressionLevel Level = CompressionLevel.Standard, bool DeferClear = false) {
    public static readonly EncodeOptions Default = new();

    /// <summary>Each pixel encoded as a literal code with no dictionary growth — produces the largest output.</summary>
    public static EncodeOptions NoCompression() => new(CompressionLevel.None);

    /// <summary>Standard LZW with optional deferred-clear-code optimisation.</summary>
    public static EncodeOptions StandardCompression(bool deferClear = false)
      => new(CompressionLevel.Standard, deferClear);

    /// <summary>Try every Standard variant (clear-now, deferred-clear) and keep the smallest output. ~2x the
    /// encode cost; saves a few % on real-world frames where deferred-clear wins or loses unpredictably.</summary>
    public static EncodeOptions BestEffort() => new(CompressionLevel.Best);
  }

  // ============================================================
  // Encoder
  // ============================================================

  /// <summary>Array overload used by tests (avoids ReadOnlySpan-vs-byte[] reflection-bridging quirks).</summary>
  internal static byte[] Encode(byte[] indexedPixels, int lzwMinCodeSize, EncodeOptions? options = null)
    => Encode((ReadOnlySpan<byte>)indexedPixels, lzwMinCodeSize, options);

  /// <summary>Encode <paramref name="indexedPixels"/> using a starting code size of <paramref name="lzwMinCodeSize"/>
  /// bits and frame the bitstream into GIF sub-blocks. The returned byte array begins with the
  /// 1-byte LZW minimum code size, then a sequence of sub-blocks (length byte + data), terminated
  /// by a zero-length block.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> indexedPixels, int lzwMinCodeSize, EncodeOptions? options = null) {
    options ??= EncodeOptions.Default;
    if (lzwMinCodeSize < 2 || lzwMinCodeSize > 8)
      throw new ArgumentOutOfRangeException(nameof(lzwMinCodeSize), "Must be 2..8.");

    return options.Level switch {
      CompressionLevel.None => _EncodeNoCompression(indexedPixels, lzwMinCodeSize),
      CompressionLevel.Best => _EncodeBest(indexedPixels, lzwMinCodeSize),
      _ => _EncodeStandard(indexedPixels, lzwMinCodeSize, options.DeferClear),
    };
  }

  /// <summary>Emits each pixel as its own literal code, never referencing dictionary-extended codes.
  /// The decoder still grows its dictionary per spec, so the encoder must track code-size growth
  /// in lockstep and emit a Clear before the table would overflow.</summary>
  private static byte[] _EncodeNoCompression(ReadOnlySpan<byte> indexedPixels, int lzwMinCodeSize) {
    var clearCode = 1 << lzwMinCodeSize;
    var eoiCode = clearCode + 1;
    var startCodeSize = lzwMinCodeSize + 1;

    using var ms = new MemoryStream();
    ms.WriteByte((byte)lzwMinCodeSize);
    using var bitOut = new _BitWriterSubBlocks(ms);

    var codeSize = startCodeSize;
    var nextCode = eoiCode + 1;
    var inRunSinceClear = false;

    bitOut.Write(clearCode, codeSize);

    for (var i = 0; i < indexedPixels.Length; ++i) {
      bitOut.Write(indexedPixels[i], codeSize);

      // Decoder side-effect: when prev >= 0 (i.e. not the first code after a clear) it allocates
      // a new dictionary entry, then bumps codeSize when nextCode hits (1 << codeSize).
      if (inRunSinceClear && nextCode < _MaxCodes) {
        ++nextCode;
        if (nextCode == (1 << codeSize) && codeSize < _MaxCodeBits) ++codeSize;
      }
      inRunSinceClear = true;

      // If the next literal would push the decoder over the dictionary cap, reset.
      if (nextCode >= _MaxCodes - 1 && i + 1 < indexedPixels.Length) {
        bitOut.Write(clearCode, codeSize);
        codeSize = startCodeSize;
        nextCode = eoiCode + 1;
        inRunSinceClear = false;
      }
    }
    bitOut.Write(eoiCode, codeSize);
    bitOut.Flush();
    return ms.ToArray();
  }

  /// <summary>Standard LZW with optional deferred-clear-code optimisation.</summary>
  private static byte[] _EncodeStandard(ReadOnlySpan<byte> indexedPixels, int lzwMinCodeSize, bool deferClear) {
    var clearCode = 1 << lzwMinCodeSize;
    var eoiCode = clearCode + 1;
    var startCodeSize = lzwMinCodeSize + 1;

    using var ms = new MemoryStream();
    ms.WriteByte((byte)lzwMinCodeSize);
    using var bitOut = new _BitWriterSubBlocks(ms);

    var dict = new Dictionary<int, int>();
    var codeSize = startCodeSize;
    var nextCode = eoiCode + 1;

    bitOut.Write(clearCode, codeSize);

    if (indexedPixels.Length == 0) {
      bitOut.Write(eoiCode, codeSize);
      bitOut.Flush();
      return ms.ToArray();
    }

    var w = (int)indexedPixels[0];
    for (var i = 1; i < indexedPixels.Length; ++i) {
      var k = indexedPixels[i];
      var combined = (w << 9) | k;
      if (dict.TryGetValue(combined, out var existing)) {
        w = existing;
        continue;
      }

      bitOut.Write(w, codeSize);
      if (nextCode < _MaxCodes) {
        dict[combined] = nextCode++;
        if (nextCode == (1 << codeSize) + 1 && codeSize < _MaxCodeBits) ++codeSize;
      } else if (!deferClear) {
        bitOut.Write(clearCode, codeSize);
        dict.Clear();
        codeSize = startCodeSize;
        nextCode = eoiCode + 1;
      }

      w = k;
    }

    bitOut.Write(w, codeSize);
    bitOut.Write(eoiCode, codeSize);
    bitOut.Flush();
    return ms.ToArray();
  }

  /// <summary>Tries every Standard variant and keeps the smallest output. Currently exhausts {clear-now,
  /// defer-clear}; future Zopfli-style additions (e.g. multi-iteration dictionary seeding) would extend
  /// the candidate list here without changing the API.</summary>
  private static byte[] _EncodeBest(ReadOnlySpan<byte> pixels, int lzwMinCodeSize) {
    var clearNow = _EncodeStandard(pixels, lzwMinCodeSize, deferClear: false);
    var deferred = _EncodeStandard(pixels, lzwMinCodeSize, deferClear: true);
    return deferred.Length < clearNow.Length ? deferred : clearNow;
  }

  // ============================================================
  // Decoder
  // ============================================================

  /// <summary>Decode the LZW sub-block bitstream at the current stream position. Reads the
  /// 1-byte LZW minimum code size first, then sub-blocks until a zero-length terminator. Returns
  /// the decoded indices (length should match the caller's expected pixel count; an oversize result
  /// is left intact and trimmed by the caller if necessary).</summary>
  public static byte[] Decode(Stream input, int expectedPixelCount) {
    var minCodeSize = input.ReadByte();
    if (minCodeSize < 0) throw new EndOfStreamException("Missing LZW minimum code size.");
    if (minCodeSize < 2 || minCodeSize > 8)
      throw new InvalidDataException($"Invalid LZW minimum code size {minCodeSize}.");

    var clearCode = 1 << minCodeSize;
    var eoiCode = clearCode + 1;
    var startCodeSize = minCodeSize + 1;

    var prefix = new int[_MaxCodes];
    var suffix = new byte[_MaxCodes];
    for (var i = 0; i < clearCode; ++i) {
      prefix[i] = -1;
      suffix[i] = (byte)i;
    }

    var bitIn = new _BitReaderSubBlocks(input);
    var output = new List<byte>(Math.Max(expectedPixelCount, 64));
    Span<byte> stack = stackalloc byte[_MaxCodes];

    int codeSize = startCodeSize;
    int prev = -1;
    int firstChar = 0;
    int nextCode = eoiCode + 1;

    while (bitIn.TryRead(codeSize, out var code)) {
      if (code == clearCode) {
        codeSize = startCodeSize;
        nextCode = eoiCode + 1;
        prev = -1;
        continue;
      }
      if (code == eoiCode) break;

      int curCode;
      var stackTop = 0;
      if (code < nextCode) {
        curCode = code;
      } else if (code == nextCode && prev >= 0) {
        // KwKwK pattern — code refers to a string we're about to add.
        stack[stackTop++] = (byte)firstChar;
        curCode = prev;
      } else {
        throw new InvalidDataException($"Invalid LZW code {code} (next={nextCode}).");
      }

      while (curCode >= 0) {
        if (stackTop >= stack.Length) throw new InvalidDataException("LZW stack overflow.");
        stack[stackTop++] = suffix[curCode];
        curCode = prefix[curCode];
      }
      firstChar = stack[stackTop - 1];

      for (var i = stackTop - 1; i >= 0; --i) output.Add(stack[i]);

      if (prev >= 0 && nextCode < _MaxCodes) {
        prefix[nextCode] = prev;
        suffix[nextCode] = (byte)firstChar;
        ++nextCode;
        if (nextCode == (1 << codeSize) && codeSize < _MaxCodeBits) ++codeSize;
      }
      prev = code;
    }

    // Drain any remaining sub-block bytes the bit reader hasn't consumed (rare but spec-legal).
    bitIn.SkipToBlockTerminator();

    return output.ToArray();
  }

  // ============================================================
  // Bit-level I/O wrapped in GIF's 1..255-byte sub-block framing.
  // ============================================================

  /// <summary>Writes a stream of variable-width bit codes into 1..255-byte sub-blocks, finalised with a
  /// zero-length terminator. The underlying <see cref="MemoryStream"/> receives the full framed payload.</summary>
  private sealed class _BitWriterSubBlocks : IDisposable {
    private readonly MemoryStream _output;
    private readonly byte[] _block = new byte[255];
    private int _blockUsed;
    private uint _bitBuffer;
    private int _bitCount;

    public _BitWriterSubBlocks(MemoryStream output) { this._output = output; }

    public void Write(int code, int bits) {
      this._bitBuffer |= (uint)code << this._bitCount;
      this._bitCount += bits;
      while (this._bitCount >= 8) {
        this._block[this._blockUsed++] = (byte)(this._bitBuffer & 0xFF);
        this._bitBuffer >>= 8;
        this._bitCount -= 8;
        if (this._blockUsed == 255) this._FlushBlock();
      }
    }

    public void Flush() {
      if (this._bitCount > 0) {
        this._block[this._blockUsed++] = (byte)(this._bitBuffer & 0xFF);
        this._bitBuffer = 0;
        this._bitCount = 0;
        if (this._blockUsed == 255) this._FlushBlock();
      }
      this._FlushBlock();
      this._output.WriteByte(0); // block terminator
    }

    private void _FlushBlock() {
      if (this._blockUsed == 0) return;
      this._output.WriteByte((byte)this._blockUsed);
      this._output.Write(this._block, 0, this._blockUsed);
      this._blockUsed = 0;
    }

    public void Dispose() { /* nothing — caller owns the stream */ }
  }

  /// <summary>Reads variable-width bit codes from GIF sub-blocks. Stops returning data when a zero-length
  /// block (the terminator) is hit.</summary>
  private sealed class _BitReaderSubBlocks {
    private readonly Stream _input;
    private readonly byte[] _block = new byte[255];
    private int _blockLen;
    private int _blockPos;
    private bool _finished;
    private uint _bitBuffer;
    private int _bitCount;

    public _BitReaderSubBlocks(Stream input) { this._input = input; }

    public bool TryRead(int bits, out int value) {
      while (this._bitCount < bits) {
        if (!this._FillBlock()) {
          value = 0;
          return false;
        }
        this._bitBuffer |= (uint)this._block[this._blockPos++] << this._bitCount;
        this._bitCount += 8;
      }
      value = (int)(this._bitBuffer & ((1u << bits) - 1));
      this._bitBuffer >>= bits;
      this._bitCount -= bits;
      return true;
    }

    private bool _FillBlock() {
      if (this._blockPos < this._blockLen) return true;
      if (this._finished) return false;
      var sizeByte = this._input.ReadByte();
      if (sizeByte <= 0) { this._finished = true; return false; }
      this._blockLen = sizeByte;
      this._blockPos = 0;
      var read = 0;
      while (read < this._blockLen) {
        var n = this._input.Read(this._block, read, this._blockLen - read);
        if (n == 0) throw new EndOfStreamException("Unexpected EOF mid-sub-block.");
        read += n;
      }
      return true;
    }

    /// <summary>Advance past any trailing sub-blocks the bit reader didn't consume (the LZW EOI code
    /// sometimes appears before all sub-block bytes are read).</summary>
    public void SkipToBlockTerminator() {
      while (!this._finished) {
        if (this._blockPos < this._blockLen) {
          this._blockPos = this._blockLen; // discard remaining bytes of this block
        } else {
          var sz = this._input.ReadByte();
          if (sz <= 0) { this._finished = true; return; }
          for (var i = 0; i < sz; ++i)
            if (this._input.ReadByte() < 0) throw new EndOfStreamException();
        }
      }
    }
  }
}
