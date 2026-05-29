using System.IO;

namespace FileFormat.Jpeg;

/// <summary>MSB-first bit writer with JPEG 0xFF/0x00 byte-stuffing insertion.</summary>
internal sealed class JpegBitWriter {
  private readonly MemoryStream _stream;
  private int _bitBuffer;
  private int _bitsInBuffer;

  public JpegBitWriter(MemoryStream stream) => this._stream = stream;

  public void WriteBits(int value, int count) {
    this._bitBuffer = (this._bitBuffer << count) | (value & ((1 << count) - 1));
    this._bitsInBuffer += count;

    while (this._bitsInBuffer >= 8) {
      this._bitsInBuffer -= 8;
      var b = (byte)((this._bitBuffer >> this._bitsInBuffer) & 0xFF);
      this._stream.WriteByte(b);
      if (b == 0xFF)
        this._stream.WriteByte(0x00); // Byte-stuffing
    }
  }

  /// <summary>Writes an encoded Huffman symbol (code + length from EhufCo/EhufSi).</summary>
  public void WriteHuffmanCode(int code, int length) => WriteBits(code, length);

  /// <summary>Flushes remaining bits, padding with 1-bits to byte boundary.</summary>
  public void FlushBits() {
    if (this._bitsInBuffer > 0) {
      var padBits = 8 - this._bitsInBuffer;
      WriteBits((1 << padBits) - 1, padBits);
    }

    this._bitBuffer = 0;
    this._bitsInBuffer = 0;
  }

  /// <summary>Counts bits that would be written for a Huffman symbol (for frequency counting pass).</summary>
  public static int CountBits(int codeLength) => codeLength;
}
