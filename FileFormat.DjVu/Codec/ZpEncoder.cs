using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FileFormat.DjVu.Codec;

/// <summary>
/// ZP (Z-coder) adaptive binary arithmetic encoder for DjVu.
/// Faithful port of djvulibre's ZPCodec encoder (ZPCODER variant).
///
/// Key: z = a + P[ctx], NOT just P[ctx].
/// Uses the z-restriction: d = 0x6000 + ((z+a)>>2); if (z > d) z = d
/// Output uses a 24-bit buffer with bit-stuffing carry propagation (zemit/outbit).
/// Initial 25-bit delay before output begins.
/// </summary>
internal sealed class ZpEncoder {

  private readonly MemoryStream _output;
  private uint _a;        // interval width above half
  private uint _subend;   // sub-interval endpoint
  private uint _buffer;   // 24-bit carry buffer
  private int _nrun;      // run of 0x00 bytes pending
  private int _delay;     // countdown before output starts (25 initially)
  private byte _byte;     // accumulating output byte
  private int _scount;    // bits accumulated in _byte

  public ZpEncoder() {
    _output = new(4096);
    _a = 0;
    _scount = 0;
    _byte = 0;
    _delay = 25;
    _subend = 0;
    _buffer = 0xFFFFFF;
    _nrun = 0;
  }

  /// <summary>Encodes a single bit given a context.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void EncodeBit(int bit, ref ZpContext ctx) {
    // z = a + P[ctx] -- the combined interval boundary
    var z = _a + ZpTables.P[ctx.Value];

    if (bit != (ctx.Value & 1)) {
      // LPS path
      // Apply z-restriction (ZPCODER variant)
      var d = 0x6000u + ((z + _a) >> 2);
      if (z > d) z = d;
      // Context demotion
      ctx = new(ZpTables.Dn[ctx.Value]);
      // Update interval: LPS region
      z = 0x10000u - z;
      _subend += z;
      _a += z;
    } else if (z >= 0x8000) {
      // MPS path, needs renorm
      // Apply z-restriction (ZPCODER variant)
      var d = 0x6000u + ((z + _a) >> 2);
      if (z > d) z = d;
      // Context promotion
      if (_a >= ZpTables.M[ctx.Value])
        ctx = new(ZpTables.Up[ctx.Value]);
      // Update interval: MPS region
      _a = z;
    } else {
      // MPS fast path: no renorm needed
      _a = z;
      return;
    }

    // Export bits (renormalize)
    while (_a >= 0x8000) {
      _Zemit(1 - (int)(_subend >> 15));
      _subend = (ushort)(_subend << 1);
      _a = (ushort)(_a << 1);
    }
  }

  /// <summary>Encodes a bit without context (equiprobable).</summary>
  public void EncodePassthrough(int bit) {
    // IW44-style passthrough: z = 0x8000 + (a >> 1)
    var z = 0x8000u + (_a >> 1);

    if (bit != 0) {
      // LPS: code 1
      z = 0x10000u - z;
      _subend += z;
      _a += z;
    } else {
      // MPS: code 0
      _a = z;
    }

    // Export bits
    while (_a >= 0x8000) {
      _Zemit(1 - (int)(_subend >> 15));
      _subend = (ushort)(_subend << 1);
      _a = (ushort)(_a << 1);
    }
  }

  /// <summary>Encodes an unsigned integer in binary with the given number of bits.</summary>
  public void EncodeBinary(int value, ref ZpContext ctx, int bits) {
    for (var i = bits - 1; i >= 0; --i)
      EncodeBit((value >> i) & 1, ref ctx);
  }

  /// <summary>Finalizes and returns compressed bytes.</summary>
  public byte[] Finish() {
    _Eflush();
    return _output.ToArray();
  }

  private void _Zemit(int b) {
    _buffer = (_buffer << 1) + (uint)b;
    b = (int)(_buffer >> 24);
    _buffer &= 0xFFFFFF;
    switch (b) {
      case 1:
        _Outbit(1);
        while (_nrun-- > 0)
          _Outbit(0);
        _nrun = 0;
        break;
      case 0xFF:
        _Outbit(0);
        while (_nrun-- > 0)
          _Outbit(1);
        _nrun = 0;
        break;
      case 0:
        ++_nrun;
        break;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Outbit(int bit) {
    if (_delay > 0) {
      if (_delay < 0xFF)
        --_delay;
    } else {
      _byte = (byte)((_byte << 1) | bit);
      if (++_scount == 8) {
        _output.WriteByte(_byte);
        _scount = 0;
        _byte = 0;
      }
    }
  }

  private void _Eflush() {
    if (_subend > 0x8000)
      _subend = 0x10000;
    else if (_subend > 0)
      _subend = 0x8000;
    while (_buffer != 0xFFFFFF || _subend != 0) {
      _Zemit(1 - (int)(_subend >> 15));
      _subend = (ushort)(_subend << 1);
    }
    _Outbit(1);
    while (_nrun-- > 0)
      _Outbit(0);
    _nrun = 0;
    while (_scount > 0)
      _Outbit(1);
    _delay = 0xFF;
  }
}
