using System;

namespace FileFormat.Core;

/// <summary>
/// Apple's PackBytes, whose command byte says both how many bytes follow and how far to step back
/// between them.
/// </summary>
/// <remarks>
/// The top two bits choose a stride of nothing, one, or four: nothing gives a run of literals, one
/// gives a byte repeated, and four gives a four-byte pattern repeated — which is exactly what a
/// dither or a run of identical pixels in a four-byte-aligned bitmap produces. The two long forms
/// multiply the count by four, so a screen of one colour costs two bytes for every 256.
/// <para/>
/// It is a stream rather than a function because a format may unpack one scanline at a time and
/// move the read position between them; a run left part-finished at the end of a line carries its
/// count into the next.
/// </remarks>
public struct PackBytesStream {

  /// <summary>How far back each of the four command kinds steps between bytes.</summary>
  private static ReadOnlySpan<int> _Strides => [0, 1, 4, 1];

  private int _count;
  private int _stride;

  /// <summary>Creates a stream reading from the given offset.</summary>
  public PackBytesStream(int offset) {
    this.Offset = offset;
    this._count = 1;
    this._stride = 0;
  }

  /// <summary>Where the next command or byte will be read from.</summary>
  public int Offset { get; set; }

  /// <summary>Reads one unpacked byte, or -1 if the stream has run out.</summary>
  public int ReadByte(ReadOnlySpan<byte> data) {
    if (--this._count == 0) {
      if (this.Offset >= data.Length)
        return -1;

      var command = data[this.Offset++];
      this._count = (command & 63) + 1;
      if (command >= 128)
        this._count <<= 2;

      this._stride = _Strides[command >> 6];
    } else if (this._stride != 0 && (this._count & (this._stride - 1)) == 0)
      this.Offset -= this._stride;

    if (this.Offset < 0 || this.Offset >= data.Length)
      return -1;

    return data[this.Offset++];
  }
}
