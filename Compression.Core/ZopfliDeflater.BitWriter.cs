using System;
using System.Buffers;

namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>LSB-first bit output stream for DEFLATE encoding</summary>
  internal sealed class BitWriter {
    private int _bitPos; // 0-7, number of bits written in current byte
    private byte[] _buffer;
    private int _bytePos;

    public BitWriter(int initialCapacity = 1024) {
      this._buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
      this._buffer.AsSpan().Clear();
    }

    /// <summary>Get current output length in bytes (including partial byte)</summary>
    public int ByteLength => this._bytePos + (this._bitPos > 0 ? 1 : 0);

    /// <summary>Write bits LSB-first</summary>
    public void WriteBits(uint value, int count) {
      // Fast path: write full bytes when byte-aligned
      while (this._bitPos == 0 && count >= 8) {
        if (this._bytePos >= this._buffer.Length)
          this._Grow();

        this._buffer[this._bytePos++] = (byte)(value & 0xFF);
        value >>= 8;
        count -= 8;
      }

      // Remaining bits: bit-by-bit
      for (var i = 0; i < count; ++i) {
        if (this._bytePos >= this._buffer.Length)
          this._Grow();

        this._buffer[this._bytePos] |= (byte)(((value >> i) & 1) << this._bitPos);
        if (++this._bitPos != 8)
          continue;

        this._bitPos = 0;
        ++this._bytePos;
      }
    }

    /// <summary>Write a Huffman code (MSB-first code, but output LSB-first per DEFLATE spec)</summary>
    public void WriteHuffmanBits(uint code, int length) {
      for (var i = length - 1; i >= 0; --i) {
        if (this._bytePos >= this._buffer.Length)
          this._Grow();

        this._buffer[this._bytePos] |= (byte)(((code >> i) & 1) << this._bitPos);
        ++this._bitPos;
        if (this._bitPos != 8)
          continue;

        this._bitPos = 0;
        ++this._bytePos;
      }
    }

    /// <summary>Align to byte boundary (pad remaining bits with zeros)</summary>
    public void AlignToByte() {
      if (this._bitPos == 0)
        return;

      this._bitPos = 0;
      ++this._bytePos;
    }

    /// <summary>Get the output as a byte array, trimmed to exact size</summary>
    public byte[] GetOutput() {
      var length = this._bytePos + (this._bitPos > 0 ? 1 : 0);
      var result = new byte[length];
      Buffer.BlockCopy(this._buffer, 0, result, 0, length);
      return result;
    }

    /// <summary>Return rented buffer</summary>
    public void Release() {
      ArrayPool<byte>.Shared.Return(this._buffer);
      this._buffer = [];
    }

    private void _Grow() {
      var oldSize = this._buffer.Length;
      var newBuffer = ArrayPool<byte>.Shared.Rent(oldSize * 2);
      Buffer.BlockCopy(this._buffer, 0, newBuffer, 0, oldSize);
      newBuffer.AsSpan(oldSize, newBuffer.Length - oldSize).Clear();
      ArrayPool<byte>.Shared.Return(this._buffer);
      this._buffer = newBuffer;
    }
  }
}
