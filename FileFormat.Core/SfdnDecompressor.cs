using System;

namespace FileFormat.Core;

/// <summary>
/// The SFDN packer, which several Atari 8-bit programs used to shrink a screen dump.
/// </summary>
/// <remarks>
/// It codes the picture as nibbles rather than bytes, and stores not the nibble but how far it sits
/// from the previous one in a sixteen-entry table the file carries. Runs of one colour therefore
/// cost a single bit each, which is what a picture drawn in flat areas mostly consists of. The
/// distance is itself coded in unary — pairs of set bits, then a stop bit and one more — so the
/// commonest distances are the shortest codes.
/// </remarks>
public static class SfdnDecompressor {

  /// <summary>The four bytes an SFDN file starts with.</summary>
  public static ReadOnlySpan<byte> Magic => "S101"u8;

  /// <summary>Offset of the sixteen-entry distance table.</summary>
  public const int TableOffset = 6;

  /// <summary>Offset of the packed bits.</summary>
  public const int DataOffset = 22;

  /// <summary>Whether the data begins with an SFDN header.</summary>
  public static bool IsSfdn(ReadOnlySpan<byte> data)
    => data.Length >= DataOffset && data[..Magic.Length].SequenceEqual(Magic);

  /// <summary>The length the header says the picture unpacks to, or -1 when there is no header.</summary>
  public static int UnpackedLength(ReadOnlySpan<byte> data)
    => IsSfdn(data) ? data[4] | (data[5] << 8) : -1;

  /// <summary>Unpacks a picture, or returns null when the data is not a well-formed SFDN file.</summary>
  public static byte[]? TryUnpack(ReadOnlySpan<byte> data, int expectedLength) {
    // Half a byte per nibble is the theoretical floor, so anything shorter cannot be genuine.
    if (!IsSfdn(data) || data.Length < DataOffset + (expectedLength >> 1))
      return null;
    if (UnpackedLength(data) != expectedLength)
      return null;

    var unpacked = new byte[expectedLength];
    var reader = new _BitReader(data, DataOffset);

    var current = reader.ReadBits(4);
    if (current < 0)
      return null;

    var high = -1;
    for (var offset = 0;;) {
      if (high < 0)
        high = current;
      else {
        unpacked[offset++] = (byte)((high << 4) | current);
        if (offset >= expectedLength)
          return unpacked;

        high = -1;
      }

      // Unary: pairs of set bits, then a stop bit and one more, giving an index into the table.
      int code = 0, bit;
      for (;; code += 2) {
        bit = reader.ReadBit();
        if (bit == 0)
          break;
        if (bit < 0 || code >= 14)
          return null;
      }

      bit = reader.ReadBit();
      if (bit < 0)
        return null;

      current = (current - data[TableOffset + code + bit]) & 15;
    }
  }

  /// <summary>Reads single bits most significant first.</summary>
  private ref struct _BitReader(ReadOnlySpan<byte> data, int offset) {

    private readonly ReadOnlySpan<byte> _data = data;
    private int _offset = offset;
    private int _bits;

    public int ReadBit() {
      // The low seven bits hold a marker that walks up as the byte is consumed; when it clears,
      // the byte is spent.
      if ((this._bits & 127) == 0) {
        if (this._offset >= this._data.Length)
          return -1;

        this._bits = (this._data[this._offset++] << 1) | 1;
      } else
        this._bits <<= 1;

      return (this._bits >> 8) & 1;
    }

    public int ReadBits(int count) {
      var result = 0;
      while (--count >= 0) {
        var bit = this.ReadBit();
        if (bit < 0)
          return -1;

        result = (result << 1) | bit;
      }

      return result;
    }
  }
}
