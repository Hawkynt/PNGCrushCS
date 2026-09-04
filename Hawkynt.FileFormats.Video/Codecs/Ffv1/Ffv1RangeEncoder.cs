using System;
using System.Numerics;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// The writing half of FFV1's binary range coder, and the coding of whole numbers on top of it
/// (RFC 9043 §3.8.1).
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/rangecoder.h</c> and <c>rangecoder.c</c>, copyright (c) 2004
/// Michael Niedermayer, and the number coding in <c>libavcodec/ffv1enc.c</c>, copyright (c)
/// 2003-2013 Michael Niedermayer, both LGPL-2.1-or-later; this adaptation is distributed with
/// PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// The mirror of <see cref="Ffv1RangeCoder"/>: the same split of the range in proportion to the
/// state, the same tables the state moves along, and the same layout of a whole number's bits over a
/// context's thirty-two states — so that whatever this writes, that reads. The one thing an encoder
/// has that a decoder does not is the carry. Narrowing the range can push the low end over a byte
/// boundary, and a byte already decided may then need one adding to it. Rather than write bytes it
/// might have to take back, the coder holds one byte and a count of <c>0xFF</c>s behind it until it
/// knows whether the carry comes; a carry turns them into that byte plus one and a run of noughts.
/// <para/>
/// Termination is the specification's, which is also ffmpeg's: the range is narrowed to a byte and
/// the coder flushed twice, and the last byte held back is not written at all. A decoder reads
/// noughts past the end of what it was given, and the arithmetic is arranged so that a nought there
/// still lands inside the final interval — which is one byte saved on every slice, and why a decoder
/// that counts what it has read ends one byte past the data.
/// </remarks>
internal sealed class Ffv1RangeEncoder {

  private readonly byte[] _zeroState;
  private readonly byte[] _oneState;
  private byte[] _bytes = new byte[4096];
  private int _length;
  private int _low;
  private int _range = 0xFF00;
  private int _outstandingCount;
  private int _outstandingByte = -1;

  internal Ffv1RangeEncoder(byte[] zeroState, byte[] oneState) {
    this._zeroState = zeroState;
    this._oneState = oneState;
  }

  /// <summary>Writes one bit against a state, and moves that state along.</summary>
  internal void Put(byte[] states, int index, int bit) {
    var state = states[index];
    var split = this._range * state >> 8;

    if (bit == 0) {
      this._range -= split;
      states[index] = this._zeroState[state];
    } else {
      this._low += this._range - split;
      this._range = split;
      states[index] = this._oneState[state];
    }

    if (this._range < 0x100)
      this._Renormalise();
  }

  /// <summary>
  /// Writes a whole number, signed or not, into one context's thirty-two states.
  /// </summary>
  /// <remarks>
  /// The same layout <see cref="Ffv1RangeCoder.Symbol"/> reads: state 0 for "is it zero", the
  /// exponent in unary over states 1 to 10, the mantissa bits over 22 to 31, and the sign over 11 to
  /// 21, each run sharing its last state once it has gone far enough.
  /// </remarks>
  internal void Symbol(byte[] states, int value, bool signed) {
    if (value == 0) {
      this.Put(states, 0, 1);
      return;
    }

    var magnitude = signed ? Math.Abs(value) : value;
    var exponent = BitOperations.Log2((uint)magnitude);

    this.Put(states, 0, 0);
    for (var i = 0; i < exponent; ++i)
      this.Put(states, 1 + Math.Min(i, 9), 1);

    this.Put(states, 1 + Math.Min(exponent, 9), 0);

    for (var i = exponent - 1; i >= 0; --i)
      this.Put(states, 22 + Math.Min(i, 9), (magnitude >> i) & 1);

    if (signed)
      this.Put(states, 11 + Math.Min(exponent, 10), value < 0 ? 1 : 0);
  }

  /// <summary>
  /// Finishes the coded run and hands back its bytes.
  /// </summary>
  /// <param name="withSentinel">
  /// Whether to write the symbol that marks the end of a slice's range-coded run first — a bit
  /// coded against state 129 and thrown away by the reader, whose purpose is that reading it leaves a
  /// decoder exactly one byte past the data. A slice has it; a configuration record does not.
  /// </param>
  internal byte[] Terminate(bool withSentinel) {
    if (withSentinel) {
      var sentinel = new byte[] { 129 };
      this.Put(sentinel, 0, 0);
    }

    this._range = 0xFF;
    this._low += 0xFF;
    this._Renormalise();
    this._range = 0xFF;
    this._Renormalise();

    return this._bytes.AsSpan(0, this._length).ToArray();
  }

  /// <summary>
  /// Shifts a byte out of the low end of the range, or holds it back if a carry might still reach it.
  /// </summary>
  private void _Renormalise() {
    if (this._low < 0xFF01 || this._low >= 0x10000) {
      var carry = this._low >= 0x10000;
      if (this._outstandingByte >= 0)
        this._Write((byte)(this._outstandingByte + (carry ? 1 : 0)));

      var fill = (byte)(carry ? 0x00 : 0xFF);
      for (; this._outstandingCount > 0; --this._outstandingCount)
        this._Write(fill);

      this._outstandingByte = (this._low >> 8) & 0xFF;
    } else
      ++this._outstandingCount;

    this._low = (this._low & 0xFF) << 8;
    this._range <<= 8;
  }

  private void _Write(byte value) {
    if (this._length == this._bytes.Length)
      Array.Resize(ref this._bytes, this._bytes.Length * 2);

    this._bytes[this._length++] = value;
  }
}
