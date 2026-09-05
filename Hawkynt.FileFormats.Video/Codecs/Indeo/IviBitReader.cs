using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Takes bits out of an Indeo 4 or Indeo 5 frame in the order the format wrote them: least
/// significant bit of the first byte first.
/// </summary>
/// <remarks>
/// Indeo is one of the few formats whose bit order is the little-endian one. A field of <i>n</i> bits
/// is the next <i>n</i> bits of the stream with the first of them as the field's <b>least</b>
/// significant bit, and a field crossing a byte boundary continues into the low bits of the next
/// byte. Reading it the other way round produces field values that are usually still in range, so
/// getting this backwards yields a picture rather than an error, which is why the order is written
/// down here rather than left implicit.
/// <para/>
/// Reading past the end of the packet yields zero bits rather than throwing. That is deliberate and
/// matches what the format's own structure needs: an Indeo frame states its own sizes — the tile data
/// size, the band data size, the number of macroblocks in a tile — and the decoder checks the
/// position against them after each unit. Those checks are what catches a frame read out of step, and
/// they can only run if a read that overshoots the last byte comes back rather than aborting.
/// <see cref="BitsLeft"/> is how the places that need to know ask.
/// </remarks>
internal sealed class IviBitReader {

  private readonly ReadOnlyMemory<byte> _data;

  internal IviBitReader(ReadOnlyMemory<byte> data) => this._data = data;

  /// <summary>How many bits into the packet the next read starts.</summary>
  internal int Position { get; private set; }

  /// <summary>The packet's length in bits.</summary>
  internal int Length => this._data.Length * 8;

  /// <summary>How many bits of the packet have not been read yet, which may be negative past the end.</summary>
  internal int BitsLeft => this.Length - this.Position;

  /// <summary>Reads one bit.</summary>
  internal int ReadBit() {
    var bit = this._BitAt(this.Position);
    ++this.Position;
    return bit;
  }

  /// <summary>Reads one bit as a flag.</summary>
  internal bool ReadFlag() => this.ReadBit() != 0;

  /// <summary>Reads a field of up to thirty-two bits.</summary>
  internal uint Read(int count) {
    var value = this.Peek(count);
    this.Position += count;
    return value;
  }

  /// <summary>Reads a field of up to thirty-two bits without advancing.</summary>
  internal uint Peek(int count) {
    if (count <= 0)
      return 0;

    var span = this._data.Span;
    var at = this.Position >> 3;
    var shift = this.Position & 7;

    // Five bytes hold any thirty-two-bit field however it straddles the byte it starts in.
    ulong window = 0;
    for (var i = 0; i < 5; ++i) {
      var index = at + i;
      if (index < span.Length)
        window |= (ulong)span[index] << (i * 8);
    }

    var mask = count >= 32 ? uint.MaxValue : (1u << count) - 1;
    return (uint)(window >> shift) & mask;
  }

  /// <summary>Skips a field without reading it.</summary>
  internal void Skip(int count) => this.Position += count;

  /// <summary>
  /// Whatever whole bytes of the packet are left, which is how Indeo 4 reaches the second frame
  /// packed behind the first.
  /// </summary>
  internal ReadOnlyMemory<byte> Remaining {
    get {
      var at = this.Position >> 3;
      return at >= this._data.Length ? ReadOnlyMemory<byte>.Empty : this._data[at..];
    }
  }

  /// <summary>Moves to the given bit position, which is how a tile is skipped to.</summary>
  internal void SeekTo(int position) => this.Position = position;

  /// <summary>Advances to the next byte boundary.</summary>
  internal void Align() => this.Position = (this.Position + 7) & ~7;

  private int _BitAt(int position) {
    var at = position >> 3;
    var span = this._data.Span;
    return at < 0 || at >= span.Length ? 0 : (span[at] >> (position & 7)) & 1;
  }
}
