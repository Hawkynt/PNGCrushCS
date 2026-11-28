namespace FileFormat.JpegXr.JxrLib;

/// <summary>
/// MSB-first bit reader over the whole file, addressed by absolute bit position.
/// Bytes outside the file read as zero, so a truncated packet degrades instead of throwing.
/// </summary>
internal sealed class JxrBitReader(byte[] data) {

  private readonly byte[] _data = data;
  private long _bitPos;

  /// <summary>False until the reader has been positioned on a packet (jxrlib's "stream attached").</summary>
  internal bool Attached { get; private set; }

  internal void Attach(long bytePosition) {
    this._bitPos = bytePosition << 3;
    this.Attached = true;
  }

  /// <summary>Releases the packet; the reader always detaches at a byte boundary.</summary>
  internal void Detach() {
    this.FlushToByte();
    this.Attached = false;
  }

  /// <summary>Byte offset of the next unread byte, ignoring a partially consumed byte.</summary>
  internal long BytePosition => this._bitPos >> 3;

  private uint _Window() {
    var p = this._bitPos >> 3;
    var d = this._data;
    uint w = 0;
    for (var i = 0; i < 4; ++i) {
      var q = p + i;
      w = (w << 8) | (q >= 0 && q < d.Length ? d[q] : 0u);
    }
    return w << (int)(this._bitPos & 7);
  }

  internal uint Peek(int bits) => bits == 0 ? 0u : this._Window() >> (32 - bits);

  internal void Flush(int bits) => this._bitPos += bits;

  internal uint Get(int bits) {
    var r = this.Peek(bits);
    this._bitPos += bits;
    return r;
  }

  internal uint GetBool() => this.Get(1);

  /// <summary>Returns 0 for a clear bit and -1 for a set bit.</summary>
  internal int GetSign() => -(int)this.Get(1);

  /// <summary>Reads a magnitude of <paramref name="bits"/> bits followed by a sign bit that is present only when the magnitude is non-zero.</summary>
  internal int GetSigned(int bits) {
    var r = (int)this.Peek(bits + 1);
    r = ((r >> 1) ^ -(r & 1)) + (r & 1);
    this._bitPos += bits + (r != 0 ? 1 : 0);
    return r;
  }

  internal uint Get32(int bits) {
    uint r = 0;
    if (bits > 16) {
      r = this.Get(16);
      bits -= 16;
      r <<= bits;
    }
    return r | this.Get(bits);
  }

  internal void FlushToByte() => this._bitPos = (this._bitPos + 7) & ~7L;
}

/// <summary>Byte-fed header reader: consumes whole bytes from the stream and hands out up to 32 bits at a time.</summary>
internal sealed class JxrHeaderBitReader(byte[] data, long bytePosition) {

  private readonly byte[] _data = data;
  private long _bitPos = bytePosition << 3;

  internal uint Get(int bits) {
    uint r = 0;
    for (var i = 0; i < bits; ++i) {
      var p = this._bitPos >> 3;
      var b = p >= 0 && p < this._data.Length ? this._data[p] : 0;
      r = (r << 1) | (uint)((b >> (7 - (int)(this._bitPos & 7))) & 1);
      ++this._bitPos;
    }
    return r;
  }

  internal void FlushToByte() => this._bitPos = (this._bitPos + 7) & ~7L;

  /// <summary>Stream position after the bytes fetched so far.</summary>
  internal long BytePosition => (this._bitPos + 7) >> 3;
}
