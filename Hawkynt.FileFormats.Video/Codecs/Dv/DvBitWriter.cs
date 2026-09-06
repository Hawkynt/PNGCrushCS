using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Writes bits most significant first into a fixed buffer, and says how much room is left.
/// </summary>
/// <remarks>
/// The remaining-room question is what this exists for. DV's block layer fills a chain of fixed
/// allowances in order — a block's own budget, then the macroblock's spare space, then the video
/// segment's — and a code that will not fit in the current one is split across the boundary rather
/// than moved past it. So a writer that silently grew, or that refused a partial write, would be the
/// wrong shape for the format.
/// <para/>
/// The buffer is zero past what has been written. A reader is handed <see cref="BitCount"/> as its
/// limit and never sees the padding, but the encoder's overflow spaces are read back as bit streams
/// of their own and a stale byte there would decode as a coefficient.
/// </remarks>
internal sealed class DvBitWriter {

  private readonly byte[] _buffer;
  private readonly int _offset;
  private readonly int _bitCapacity;
  private int _bitCount;

  internal DvBitWriter(byte[] buffer, int offset, int byteCapacity) {
    ArgumentNullException.ThrowIfNull(buffer);
    this._buffer = buffer;
    this._offset = offset;
    this._bitCapacity = byteCapacity * 8;
    Array.Clear(buffer, offset, byteCapacity);
  }

  /// <summary>How many bits have been written.</summary>
  internal int BitCount => this._bitCount;

  /// <summary>How many bits the buffer still holds.</summary>
  internal int BitsLeft => this._bitCapacity - this._bitCount;

  /// <summary>The bytes written so far, as a reader over exactly them.</summary>
  internal ReadOnlySpan<byte> Written => this._buffer.AsSpan(this._offset, (this._bitCount + 7) / 8);

  /// <summary>
  /// Writes the low <paramref name="count"/> bits of a value, most significant first.
  /// </summary>
  internal void Put(int count, uint value) {
    if (count <= 0)
      return;
    if (count > this.BitsLeft)
      throw new InvalidOperationException($"A DV bit buffer of {this._bitCapacity} bits was asked for {count} more than it holds.");

    // Masked because callers hand over whole code words whose unused high bits are not always clear.
    if (count < 32)
      value &= (1u << count) - 1;

    var index = this._bitCount;
    this._bitCount += count;

    while (count > 0) {
      var byteIndex = this._offset + (index >> 3);
      var free = 8 - (index & 7);
      var take = Math.Min(free, count);
      var bits = (byte)((value >> (count - take)) & ((1u << take) - 1));
      this._buffer[byteIndex] |= (byte)(bits << (free - take));
      index += take;
      count -= take;
    }
  }

  /// <summary>Fills the rest of the buffer with a byte value, from the next whole byte on.</summary>
  /// <remarks>
  /// DV pads a block's unused tail with <c>0xff</c>, which the code table reads as the longest
  /// possible escape and a decoder therefore never mistakes for a coefficient.
  /// </remarks>
  internal void PadRemaining(byte value) {
    var from = this._offset + ((this._bitCount + 7) / 8);
    var to = this._offset + (this._bitCapacity / 8);
    for (var i = from; i < to; ++i)
      this._buffer[i] = value;
  }

  /// <summary>Copies what is left of a reader into this writer, bit for bit.</summary>
  /// <remarks>
  /// Clamped to what the destination still holds. The budgets make an overflow impossible in a
  /// well-formed frame — the leftovers of five macroblocks cannot exceed the space five macroblocks
  /// were allotted — but a truncated or scrambled packet must not take the decoder down with it.
  /// </remarks>
  internal void CopyFrom(in DvBitReader reader, int bitIndex) {
    for (var left = Math.Min(reader.BitLimit - bitIndex, this.BitsLeft); left > 0; left -= 24) {
      var take = Math.Min(24, left);
      this.Put(take, reader.PeekWord(bitIndex) >> (32 - take));
      bitIndex += take;
    }
  }
}
