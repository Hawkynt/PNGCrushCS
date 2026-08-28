namespace SharpAstro.Jxr;

/// <summary>
/// MSB-first append-only bit writer for the JPEG XR codestream — T.832 §5.3.1
/// specifies that the leftmost bit of every syntax element is the MSB. Bits
/// pack into bytes so that <c>byte[0]</c> bit 7 is the first bit of the stream.
/// </summary>
/// <remarks>
/// The writer grows its backing buffer geometrically. Call <see cref="ToArray"/>
/// at the end of encoding to retrieve the byte sequence; any partial trailing
/// byte is zero-padded in the low bits per T.832 convention.
/// </remarks>
public sealed class BitWriter
{
    private byte[] _buffer;
    private int _byteOffset;  // current byte index (0-based)
    private int _bitOffset;   // bits already filled in _buffer[_byteOffset], 0..7

    public BitWriter(int initialCapacity = 256)
    {
        _buffer = new byte[Math.Max(initialCapacity, 16)];
    }

    /// <summary>Total bits written so far.</summary>
    public int BitPosition => _byteOffset * 8 + _bitOffset;

    /// <summary>Bytes that <see cref="ToArray"/> would return, including any partial trailing byte.</summary>
    public int ByteCount => _bitOffset == 0 ? _byteOffset : _byteOffset + 1;

    /// <summary>Append one bit to the stream.</summary>
    public void WriteBit(bool bit)
    {
        EnsureCapacity(_byteOffset + 1);
        if (bit)
            _buffer[_byteOffset] |= (byte)(1 << (7 - _bitOffset));
        _bitOffset++;
        if (_bitOffset == 8)
        {
            _bitOffset = 0;
            _byteOffset++;
        }
    }

    /// <summary>
    /// Append the lowest <paramref name="length"/> bits of <paramref name="value"/>
    /// to the stream, MSB first. <paramref name="length"/> must be in <c>[0, 32]</c>;
    /// length 0 is a no-op (useful for FLC fields whose width is 0 in some contexts).
    /// </summary>
    public void WriteBits(uint value, int length)
    {
        if ((uint)length > 32) throw new ArgumentOutOfRangeException(nameof(length));
        for (var i = length - 1; i >= 0; i--)
            WriteBit(((value >> i) & 1) != 0);
    }

    /// <summary>
    /// Append a signed value as a sign bit followed by abs(value) coded in
    /// <paramref name="absLength"/> bits. Convenience wrapper for the
    /// SIGN_FLAG + magnitude pattern used by DC and AC coefficient encoding.
    /// </summary>
    public void WriteSignedMagnitude(int value, int absLength)
    {
        WriteBit(value < 0);
        WriteBits((uint)(value < 0 ? -value : value), absLength);
    }

    /// <summary>
    /// Return a copy of the encoded bytes. Any partial trailing byte is
    /// padded with zero bits in its low positions.
    /// </summary>
    public byte[] ToArray()
    {
        var len = ByteCount;
        var result = new byte[len];
        Array.Copy(_buffer, result, len);
        return result;
    }

    /// <summary>
    /// View of the encoded bytes without copying. The trailing byte (if
    /// partial) is included with its tail bits at zero.
    /// </summary>
    public ReadOnlySpan<byte> AsSpan() => _buffer.AsSpan(0, ByteCount);

    private void EnsureCapacity(int requiredBytes)
    {
        if (requiredBytes <= _buffer.Length) return;
        var newSize = _buffer.Length * 2;
        while (newSize < requiredBytes) newSize *= 2;
        Array.Resize(ref _buffer, newSize);
    }
}
