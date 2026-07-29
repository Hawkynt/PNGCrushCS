using System;
using System.Runtime.CompilerServices;

namespace FileFormat.DjVu.Codec;

/// <summary>
/// ZP (Z-coder) adaptive binary arithmetic decoder for DjVu.
/// Faithful port of djvulibre's ZPCodec decoder (ZPCODER variant).
///
/// Key: z = a + P[ctx], NOT just P[ctx].
/// Uses the z-restriction: d = 0x6000 + ((z+a)>>2); if (z > d) z = d
/// Reads from a byte stream using a 32-bit shift buffer.
/// Initial 16-bit code load, with fence optimization for fast MPS path.
/// </summary>
internal sealed class ZpDecoder {

  // Lookup table for find-first-zero (leading 1-bits count per byte)
  private static readonly byte[] _Ffzt;

  static ZpDecoder() {
    _Ffzt = new byte[256];
    for (var i = 0; i < 256; ++i) {
      _Ffzt[i] = 0;
      for (var j = i; (j & 0x80) != 0; j <<= 1)
        ++_Ffzt[i];
    }
  }

  private readonly byte[] _data;
  private int _bytePos;
  private uint _a;        // interval width above half
  private uint _code;     // code register (16-bit)
  private uint _fence;    // optimization: min(code, 0x7FFF)
  private uint _buffer;   // shift register
  private int _scount;    // bits available in buffer
  private int _delay;     // EOF delay counter

  /// <summary>Whether the decoder has exhausted the input.</summary>
  public bool IsEof => _bytePos >= _data.Length && _delay <= 0;

  public ZpDecoder(byte[] data, int startOffset = 0) {
    ArgumentNullException.ThrowIfNull(data);
    _data = data;
    _bytePos = startOffset;

    // Initialize (matching djvulibre decode init)
    _a = 0;
    _buffer = 0;
    _scount = 0;
    _delay = 25;

    // Read first two bytes into code
    var b0 = _ReadNextByte();
    _code = (uint)(b0 << 8);
    var b1 = _ReadNextByte();
    _code |= b1;

    // Preload buffer
    _Preload();

    // Set fence
    _fence = _code;
    if (_code >= 0x8000)
      _fence = 0x7FFF;
  }

  /// <summary>Decodes a single bit given a context.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeBit(ref ZpContext ctx) {
    // z = a + P[ctx] -- the combined interval boundary
    var z = _a + ZpTables.P[ctx.Value];
    if (z <= _fence) {
      // Fast MPS path: z <= fence guarantees z <= code and z < 0x8000
      // No shift needed -- lazy renormalization defers to slow path
      _a = z;
      return ctx.Value & 1;
    }
    return _DecodeSub(ref ctx, z);
  }

  /// <summary>Decodes a bit without context (equiprobable).</summary>
  public int DecodePassthrough() {
    // IW44-style passthrough: z = 0x8000 + (a >> 1)
    var z = 0x8000u + (_a >> 1);

    if (z > _code) {
      // LPS: bit 1
      z = 0x10000u - z;
      _a += z;
      _code += z;
      var shift = _Ffz(_a);
      _scount -= shift;
      _a = (ushort)(_a << shift);
      _code = (ushort)((_code << shift) | ((_buffer >> _scount) & ((1u << shift) - 1)));
      if (_scount < 16) _Preload();
      _fence = _code;
      if (_code >= 0x8000) _fence = 0x7FFF;
      return 1;
    }

    // MPS: bit 0
    --_scount;
    _a = (ushort)(z << 1);
    _code = (ushort)((_code << 1) | ((_buffer >> _scount) & 1));
    if (_scount < 16) _Preload();
    _fence = _code;
    if (_code >= 0x8000) _fence = 0x7FFF;
    return 0;
  }

  /// <summary>Decodes an unsigned integer in binary with the given number of bits.</summary>
  public int DecodeBinary(ref ZpContext ctx, int bits) {
    var value = 0;
    for (var i = bits - 1; i >= 0; --i) {
      var bit = DecodeBit(ref ctx);
      value |= bit << i;
    }
    return value;
  }

  /// <summary>Full decode path with z-restriction and LPS check.</summary>
  private int _DecodeSub(ref ZpContext ctx, uint z) {
    var bit = ctx.Value & 1;

    // Apply z-restriction (ZPCODER variant)
    var d = 0x6000u + ((z + _a) >> 2);
    if (z > d) z = d;

    if (z > _code) {
      // LPS path
      bit ^= 1;
      z = 0x10000u - z;
      _a += z;
      _code += z;
      ctx = new(ZpTables.Dn[ctx.Value]);
      var shift = _Ffz(_a);
      _scount -= shift;
      _a = (ushort)(_a << shift);
      _code = (ushort)((_code << shift) | ((_buffer >> _scount) & ((1u << shift) - 1)));
    } else {
      // MPS path (but needed renorm since z was > fence)
      if (_a >= ZpTables.M[ctx.Value])
        ctx = new(ZpTables.Up[ctx.Value]);
      --_scount;
      _a = (ushort)(z << 1);
      _code = (ushort)((_code << 1) | ((_buffer >> _scount) & 1));
    }

    if (_scount < 16) _Preload();
    _fence = _code;
    if (_code >= 0x8000) _fence = 0x7FFF;
    return bit;
  }

  /// <summary>Preload bytes into the shift buffer until scount > 24.</summary>
  private void _Preload() {
    while (_scount <= 24) {
      var b = _ReadNextByte();
      _buffer = (_buffer << 8) | b;
      _scount += 8;
    }
  }

  /// <summary>Read the next byte from the data, returning 0xFF on EOF.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private byte _ReadNextByte() {
    if (_bytePos < _data.Length)
      return _data[_bytePos++];
    if (--_delay < 1)
      return 0xFF;
    return 0xFF;
  }

  /// <summary>
  /// Find first zero bit from MSB in a 16-bit value.
  /// Returns the number of leading consecutive 1-bits.
  /// Uses the precomputed lookup table matching djvulibre's ffz().
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _Ffz(uint x)
    => x >= 0xFF00
      ? _Ffzt[x & 0xFF] + 8
      : _Ffzt[(x >> 8) & 0xFF];
}
