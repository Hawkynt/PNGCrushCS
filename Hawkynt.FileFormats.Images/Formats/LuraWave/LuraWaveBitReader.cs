using System;
using System.Buffers.Binary;

namespace FileFormat.LuraWave;

/// <summary>Why the bit reader stopped handing out stream bits.</summary>
internal enum LuraWaveReadState {

  /// <summary>Bits are flowing.</summary>
  Reading = 0,

  /// <summary>The coded data ran out, or the pass budget was spent.</summary>
  Exhausted = 1,

  /// <summary>The all-ones stripe terminator word was met.</summary>
  StripeEnd = 2,
}

/// <summary>
/// Reads the coded data as big-endian 32-bit words, most significant bit first, and interprets the word-level escapes.
/// </summary>
/// <remarks>
/// Any word of the form <c>FFFFFFxx</c> is a control word: <c>FFFFFFFF</c> ends the stripe, <c>FFFFFF00</c> and
/// <c>FFFFFFFE</c> say the next word is data even though it may look like a control word, and every other
/// <c>FFFFFFxx</c> is filler that carries no bits. Once the reader has stopped, every further bit is zero.
/// </remarks>
internal sealed class LuraWaveBitReader(byte[] data, int start) {

  private int _position = start;
  private int _register;
  private int _bitsLeft;

  public LuraWaveReadState State { get; set; }

  /// <summary>Byte offset of the next word.</summary>
  public int Position => this._position;

  /// <summary>Forgets any buffered bits so the next bit comes from a fresh word.</summary>
  public void ResetBits() {
    this._register = 0;
    this._bitsLeft = 0;
  }

  public bool ReadBit() {
    this._register <<= 1;
    if (this._bitsLeft-- == 0) {
      this._register = this.NextDataWord();
      this._bitsLeft = 31;
    }

    return this._register < 0;
  }

  /// <summary>Reads <paramref name="count"/> bits, the first one being the least significant.</summary>
  public int ReadBitsLsbFirst(int count) {
    var value = 0;
    for (var i = 0; i < count && this.State == LuraWaveReadState.Reading; ++i)
      value |= (this.ReadBit() ? 1 : 0) << i;
    return value;
  }

  /// <summary>Discards words up to and including the next stripe terminator.</summary>
  /// <returns><see langword="false"/> when the data ends first.</returns>
  public bool SkipToStripeEnd() {
    while (this.State != LuraWaveReadState.StripeEnd) {
      this.NextDataWord();
      if (this.State == LuraWaveReadState.Exhausted)
        return false;
    }

    return true;
  }

  private int NextDataWord() {
    while (true) {
      if (this.State != LuraWaveReadState.Reading)
        return 0;

      var word = this.NextRawWord();
      if (this.State != LuraWaveReadState.Reading)
        return 0;

      if ((word & ~0xFF) != ~0xFF)
        return word;

      switch (word & 0xFF) {
        case 0xFF:
          this.ResetBits();
          this.State = LuraWaveReadState.StripeEnd;
          return 0;
        case 0x00 or 0xFE:
          // An escaped word is taken as it is, even when the data ends there.
          return this.NextRawWord();
      }
    }
  }

  private int NextRawWord() {
    var at = this._position;
    this._position += 4;
    if (at + 4 > data.Length) {
      this.State = LuraWaveReadState.Exhausted;
      return -1;
    }

    return BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at, 4));
  }
}
