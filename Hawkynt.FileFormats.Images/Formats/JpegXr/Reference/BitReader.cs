namespace SharpAstro.Jxr;

/// <summary>
/// MSB-first bit reader for the JPEG XR codestream — counterpart to
/// <see cref="BitWriter"/>. T.832 §5.3.1 requires the leftmost bit of every
/// syntax element be parsed first.
/// </summary>
/// <remarks>
/// Declared as a <c>ref struct</c> so it can hold a <see cref="ReadOnlySpan{T}"/>
/// without forcing the caller into an allocation. Position is tracked
/// internally; on overrun the reader throws <see cref="EndOfStreamException"/>.
/// </remarks>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _byteOffset;
    private int _bitOffset;

    public BitReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _byteOffset = 0;
        _bitOffset = 0;
    }

    /// <summary>Total bits consumed so far.</summary>
    public readonly int BitPosition => _byteOffset * 8 + _bitOffset;

    /// <summary>Current byte-aligned read position (valid after <see cref="AlignToByte"/>-style use).
    /// Used by FREQUENCY-mode decode to anchor the per-band packet offsets from the index table.</summary>
    public readonly int BytePosition => _byteOffset;

    /// <summary>True when no more bits remain to read.</summary>
    public readonly bool IsExhausted => _byteOffset >= _buffer.Length;

    /// <summary>Consume and return one bit (true == 1, false == 0).</summary>
    public bool ReadBit()
    {
        if (_byteOffset >= _buffer.Length)
            throw new EndOfStreamException("BitReader: read past end of buffer");

        var bit = (_buffer[_byteOffset] >> (7 - _bitOffset)) & 1;
        _bitOffset++;
        if (_bitOffset == 8)
        {
            _bitOffset = 0;
            _byteOffset++;
        }
        return bit != 0;
    }

    /// <summary>
    /// Consume <paramref name="length"/> bits MSB-first and assemble them into
    /// an unsigned integer (first bit read becomes the MSB of the result).
    /// </summary>
    public uint ReadBits(int length)
    {
        if ((uint)length > 32) throw new ArgumentOutOfRangeException(nameof(length));
        uint v = 0;
        for (var i = 0; i < length; i++)
            v = (v << 1) | (ReadBit() ? 1u : 0u);
        return v;
    }

    /// <summary>
    /// Read sign bit + <paramref name="absLength"/>-bit magnitude — counterpart
    /// to <see cref="BitWriter.WriteSignedMagnitude"/>.
    /// </summary>
    public int ReadSignedMagnitude(int absLength)
    {
        var negative = ReadBit();
        var magnitude = (int)ReadBits(absLength);
        return negative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Peek at the next <paramref name="length"/> bits as an unsigned integer
    /// without consuming them. Used during VLC table prefix-matching where
    /// the table tells us how many bits to actually consume after we find
    /// the matching code.
    /// </summary>
    public readonly uint PeekBits(int length)
    {
        if ((uint)length > 32) throw new ArgumentOutOfRangeException(nameof(length));
        // Operate on a temporary copy of the cursor.
        var byteOff = _byteOffset;
        var bitOff = _bitOffset;
        uint v = 0;
        for (var i = 0; i < length; i++)
        {
            if (byteOff >= _buffer.Length)
                throw new EndOfStreamException("BitReader.PeekBits: not enough bits remaining");
            var bit = (_buffer[byteOff] >> (7 - bitOff)) & 1;
            v = (v << 1) | (uint)bit;
            bitOff++;
            if (bitOff == 8) { bitOff = 0; byteOff++; }
        }
        return v;
    }

    /// <summary>Skip <paramref name="length"/> bits without using them.</summary>
    public void SkipBits(int length)
    {
        if ((uint)length > 32) throw new ArgumentOutOfRangeException(nameof(length));
        for (var i = 0; i < length; i++) ReadBit();
    }

    /// <summary>
    /// Reposition the reader at a byte-aligned offset from the start of the
    /// underlying buffer. Used by random-access tile decode where
    /// INDEX_TABLE_TILES gives byte offsets to jump to.
    /// </summary>
    public void SeekToByte(int byteOffset)
    {
        if ((uint)byteOffset > (uint)_buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(byteOffset),
                $"byte offset {byteOffset} is past buffer length {_buffer.Length}");
        _byteOffset = byteOffset;
        _bitOffset = 0;
    }
}
