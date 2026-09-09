using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FileFormat.DjVu.Codec;

/// <summary>DjVu ZP adaptive binary arithmetic encoder.</summary>
/// <remarks>
/// The interval split, probability-state adaptation and bit emission follow the published ZP coding
/// process. Implementation provenance and compatible reference material are recorded in
/// <c>UPSTREAM.md</c> beside this file.
/// </remarks>
internal sealed class ZpEncoder {

  private readonly MemoryStream _output = new(4096);
  private uint _interval;
  private uint _subintervalEnd;
  private uint _carryWindow = 0xffffff;
  private int _pendingRun;
  private int _startupDelay = 25;
  private byte _outputByte;
  private int _outputBits;

  /// <summary>Encodes one context-adaptive bit.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void EncodeBit(int bit, ref ZpContext context) {
    var state = context.Value;
    var probableBit = state & 1;
    var split = _interval + ZpTables.P[state];

    if (bit != probableBit) {
      split = _RestrictSplit(split);
      context = new(ZpTables.Dn[state]);
      _SelectLessProbable(split);
      return;
    }

    if (split < 0x8000) {
      _interval = split;
      return;
    }

    split = _RestrictSplit(split);
    if (_interval >= ZpTables.M[state])
      context = new(ZpTables.Up[state]);

    _interval = split;
    _Renormalize();
  }

  /// <summary>Encodes one equiprobable bit.</summary>
  public void EncodePassthrough(int bit) {
    var split = 0x8000u + (_interval >> 1);
    if (bit == 0)
      _interval = split;
    else
      _SelectLessProbableWithoutRenormalization(split);

    _Renormalize();
  }

  /// <summary>Encodes an unsigned integer, most-significant bit first, with one adaptive context.</summary>
  public void EncodeBinary(int value, ref ZpContext context, int bits) {
    for (var bit = bits - 1; bit >= 0; --bit)
      EncodeBit((value >> bit) & 1, ref context);
  }

  /// <summary>Finalizes the arithmetic stream and returns the emitted bytes.</summary>
  public byte[] Finish() {
    if (_subintervalEnd > 0x8000)
      _subintervalEnd = 0x10000;
    else if (_subintervalEnd > 0)
      _subintervalEnd = 0x8000;

    while (_carryWindow != 0xffffff || _subintervalEnd != 0) {
      _EmitArithmeticBit(1 - (int)(_subintervalEnd >> 15));
      _subintervalEnd = (ushort)(_subintervalEnd << 1);
    }

    _WriteBit(1);
    _FlushPendingRun(0);

    while (_outputBits > 0)
      _WriteBit(1);

    _startupDelay = 0xff;
    return _output.ToArray();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private uint _RestrictSplit(uint split) {
    var limit = 0x6000u + ((_interval + split) >> 2);
    return Math.Min(split, limit);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _SelectLessProbable(uint split) {
    _SelectLessProbableWithoutRenormalization(split);
    _Renormalize();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _SelectLessProbableWithoutRenormalization(uint split) {
    var tail = 0x10000u - split;
    _subintervalEnd += tail;
    _interval += tail;
  }

  private void _Renormalize() {
    while (_interval >= 0x8000) {
      _EmitArithmeticBit(1 - (int)(_subintervalEnd >> 15));
      _subintervalEnd = (ushort)(_subintervalEnd << 1);
      _interval = (ushort)(_interval << 1);
    }
  }

  private void _EmitArithmeticBit(int bit) {
    _carryWindow = (_carryWindow << 1) + (uint)bit;
    var carry = _carryWindow >> 24;
    _carryWindow &= 0xffffff;

    switch (carry) {
      case 1:
        _WriteBit(1);
        _FlushPendingRun(0);
        break;
      case 0xff:
        _WriteBit(0);
        _FlushPendingRun(1);
        break;
      case 0:
        ++_pendingRun;
        break;
    }
  }

  private void _FlushPendingRun(int bit) {
    while (_pendingRun > 0) {
      --_pendingRun;
      _WriteBit(bit);
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _WriteBit(int bit) {
    if (_startupDelay > 0) {
      if (_startupDelay < 0xff)
        --_startupDelay;
      return;
    }

    _outputByte = (byte)((_outputByte << 1) | bit);
    if (++_outputBits != 8)
      return;

    _output.WriteByte(_outputByte);
    _outputByte = 0;
    _outputBits = 0;
  }
}
