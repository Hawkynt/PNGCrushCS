using System;
using System.Runtime.CompilerServices;

namespace FileFormat.WebP.Vp8L;

/// <summary>LSB-first bit reader with a 64-bit buffer for reading VP8L bitstreams.</summary>
internal sealed class Vp8LBitReader {
  private readonly byte[] _data;
  private int _bytePos;
  private ulong _buffer;
  private int _bitsInBuffer;

  public Vp8LBitReader(byte[] data, int offset) {
    this._data = data ?? throw new ArgumentNullException(nameof(data));
    this._bytePos = offset;
    _Fill();
  }

  public bool IsAtEnd => this._bytePos >= this._data.Length && this._bitsInBuffer == 0;

  /// <summary>Read <paramref name="n"/> bits (1-32), LSB first.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public uint ReadBits(int n) {
    if (this._bitsInBuffer < n)
      _Fill();

    var value = (uint)(this._buffer & ((1UL << n) - 1));
    this._buffer >>= n;
    this._bitsInBuffer -= n;
    return value;
  }

  /// <summary>Peek at next <paramref name="n"/> bits without consuming them.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public uint PeekBits(int n) {
    if (this._bitsInBuffer < n)
      _Fill();

    return (uint)(this._buffer & ((1UL << n) - 1));
  }

  /// <summary>Skip <paramref name="n"/> bits.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SkipBits(int n) {
    if (this._bitsInBuffer < n)
      _Fill();

    this._buffer >>= n;
    this._bitsInBuffer -= n;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Fill() {
    while (this._bitsInBuffer <= 56 && this._bytePos < this._data.Length) {
      this._buffer |= (ulong)this._data[this._bytePos] << this._bitsInBuffer;
      ++this._bytePos;
      this._bitsInBuffer += 8;
    }
  }
}
