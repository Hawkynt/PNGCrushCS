using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpAstro.Jxr;

/// <summary>
/// 16-byte PIXEL_FORMAT GUID identifying the on-disk pixel layout of a JXR image
/// (T.832 Annex A clause A.7.18 / Table A.6).
/// </summary>
/// <remarks>
/// Bytes are stored in the on-disk order specified by Table A.6 — left-to-right
/// as the hexadecimal string reads — not the rearranged Microsoft GUID form.
/// All standard JXR pixel formats share the 15-byte prefix
/// <c>24 C3 DD 6F 03 4E FE 4B B1 85 3D 77 76 8D C9</c> and differ only in the
/// final byte; the named constants below capture every format defined in
/// Table A.6 of T.832 (06/2019).
/// </remarks>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct JxrPixelFormat : IEquatable<JxrPixelFormat>
{
    [InlineArray(16)]
    private struct Storage { private byte _b0; }

    private readonly Storage _bytes;

    /// <summary>Construct from a 16-byte sequence in T.832 Table A.6 on-disk order.</summary>
    public JxrPixelFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException($"PIXEL_FORMAT must be exactly 16 bytes, got {bytes.Length}", nameof(bytes));
        _bytes = default;
        bytes.CopyTo(((Span<byte>)_bytes));
    }

    /// <summary>Copy the GUID bytes into <paramref name="dest"/> in on-disk order.</summary>
    public void WriteTo(Span<byte> dest)
    {
        if (dest.Length < 16) throw new ArgumentException("Destination too small", nameof(dest));
        ((ReadOnlySpan<byte>)_bytes).CopyTo(dest);
    }

    /// <summary>Snapshot the GUID as a 16-byte array.</summary>
    public byte[] ToArray()
    {
        var r = new byte[16];
        WriteTo(r);
        return r;
    }

    public bool Equals(JxrPixelFormat other) =>
        ((ReadOnlySpan<byte>)_bytes).SequenceEqual((ReadOnlySpan<byte>)other._bytes);

    public override bool Equals(object? obj) => obj is JxrPixelFormat other && Equals(other);

    public override int GetHashCode()
    {
        // 16 bytes = two 64-bit chunks; combine.
        ReadOnlySpan<byte> s = _bytes;
        var lo = BitConverter.ToUInt64(s[..8]);
        var hi = BitConverter.ToUInt64(s.Slice(8, 8));
        return HashCode.Combine(lo, hi);
    }

    public static bool operator ==(JxrPixelFormat a, JxrPixelFormat b) => a.Equals(b);
    public static bool operator !=(JxrPixelFormat a, JxrPixelFormat b) => !a.Equals(b);

    public override string ToString()
    {
        ReadOnlySpan<byte> s = _bytes;
        return Convert.ToHexString(s);
    }

    // --- Named formats from T.832 Table A.6 ---------------------------------
    //
    // All formats share the 15-byte common prefix. The last byte is unique
    // per format and is the only thing the named factory below varies.

    private static readonly byte[] CommonPrefix =
    [
        0x24, 0xC3, 0xDD, 0x6F, 0x03, 0x4E, 0xFE, 0x4B,
        0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9,
    ];

    private static JxrPixelFormat Std(byte lastByte)
    {
        Span<byte> bytes = stackalloc byte[16];
        CommonPrefix.CopyTo(bytes);
        bytes[15] = lastByte;
        return new JxrPixelFormat(bytes);
    }

    // 8-bit per channel RGB family
    public static readonly JxrPixelFormat BlackWhite           = Std(0x05);
    public static readonly JxrPixelFormat Gray8Bpp             = Std(0x08);
    public static readonly JxrPixelFormat Bgr16Bpp555          = Std(0x09);
    public static readonly JxrPixelFormat Bgr16Bpp565          = Std(0x0A);
    public static readonly JxrPixelFormat Gray16Bpp            = Std(0x0B);
    public static readonly JxrPixelFormat Bgr24Bpp             = Std(0x0C);
    public static readonly JxrPixelFormat Rgb24Bpp             = Std(0x0D);
    public static readonly JxrPixelFormat Bgr32Bpp             = Std(0x0E);
    public static readonly JxrPixelFormat Bgra32Bpp            = Std(0x0F);
    public static readonly JxrPixelFormat Pbgra32Bpp           = Std(0x10);
    public static readonly JxrPixelFormat GrayFloat32Bpp       = Std(0x11);
    public static readonly JxrPixelFormat RgbFixedPoint48Bpp   = Std(0x12);
    public static readonly JxrPixelFormat GrayFixedPoint16Bpp  = Std(0x13);   // BD16S — signed 16-bit gray
    public static readonly JxrPixelFormat Bgr101010_32Bpp      = Std(0x14);
    public static readonly JxrPixelFormat Rgb48Bpp             = Std(0x15);
    public static readonly JxrPixelFormat Rgba64Bpp            = Std(0x16);
    public static readonly JxrPixelFormat Prgba64Bpp           = Std(0x17);
    public static readonly JxrPixelFormat RgbFixedPoint96Bpp   = Std(0x18);
    public static readonly JxrPixelFormat RgbaFloat128Bpp      = Std(0x19);
    public static readonly JxrPixelFormat PrgbaFloat128Bpp     = Std(0x1A);
    public static readonly JxrPixelFormat RgbFloat128Bpp       = Std(0x1B);
    public static readonly JxrPixelFormat Cmyk32Bpp            = Std(0x1C);
    public static readonly JxrPixelFormat RgbaFixedPoint64Bpp  = Std(0x1D);
    public static readonly JxrPixelFormat RgbaFixedPoint128Bpp = Std(0x1E);
    public static readonly JxrPixelFormat Cmyk64Bpp            = Std(0x1F);
    public static readonly JxrPixelFormat Channels3_24Bpp      = Std(0x20);
    public static readonly JxrPixelFormat Channels4_32Bpp      = Std(0x21);
    public static readonly JxrPixelFormat Channels5_40Bpp      = Std(0x22);
    public static readonly JxrPixelFormat Channels6_48Bpp      = Std(0x23);
    public static readonly JxrPixelFormat Channels7_56Bpp      = Std(0x24);
    public static readonly JxrPixelFormat Channels8_64Bpp      = Std(0x25);
    public static readonly JxrPixelFormat Channels3_48Bpp      = Std(0x26);
    public static readonly JxrPixelFormat Channels4_64Bpp      = Std(0x27);
    public static readonly JxrPixelFormat Channels5_80Bpp      = Std(0x28);
    public static readonly JxrPixelFormat Channels6_96Bpp      = Std(0x29);
    public static readonly JxrPixelFormat Channels7_112Bpp     = Std(0x2A);
    public static readonly JxrPixelFormat Channels8_128Bpp     = Std(0x2B);
    public static readonly JxrPixelFormat CmykAlpha40Bpp       = Std(0x2C);
    public static readonly JxrPixelFormat CmykAlpha80Bpp       = Std(0x2D);
    public static readonly JxrPixelFormat ChannelsAlpha3_32Bpp = Std(0x2E);
    public static readonly JxrPixelFormat ChannelsAlpha4_40Bpp = Std(0x2F);
    public static readonly JxrPixelFormat ChannelsAlpha5_48Bpp = Std(0x30);
    public static readonly JxrPixelFormat ChannelsAlpha6_56Bpp = Std(0x31);
    public static readonly JxrPixelFormat ChannelsAlpha7_64Bpp = Std(0x32);
    public static readonly JxrPixelFormat ChannelsAlpha8_72Bpp = Std(0x33);
    public static readonly JxrPixelFormat ChannelsAlpha3_64Bpp  = Std(0x34);
    public static readonly JxrPixelFormat ChannelsAlpha4_80Bpp  = Std(0x35);
    public static readonly JxrPixelFormat ChannelsAlpha5_96Bpp  = Std(0x36);
    public static readonly JxrPixelFormat ChannelsAlpha6_112Bpp = Std(0x37);
    public static readonly JxrPixelFormat ChannelsAlpha7_128Bpp = Std(0x38);
    public static readonly JxrPixelFormat ChannelsAlpha8_144Bpp = Std(0x39);
    public static readonly JxrPixelFormat RgbaHalf64Bpp        = Std(0x3A);
    public static readonly JxrPixelFormat RgbHalf48Bpp         = Std(0x3B);
    public static readonly JxrPixelFormat Rgbe32Bpp            = Std(0x3D);
    public static readonly JxrPixelFormat GrayHalf16Bpp        = Std(0x3E);
    public static readonly JxrPixelFormat GrayFixedPoint32Bpp  = Std(0x3F);
    public static readonly JxrPixelFormat RgbFixedPoint64Bpp   = Std(0x40);
    public static readonly JxrPixelFormat RgbFixedPoint128Bpp  = Std(0x41);
    public static readonly JxrPixelFormat RgbHalf64Bpp         = Std(0x42);
    public static readonly JxrPixelFormat CmykDirectAlpha80Bpp = Std(0x43);
    public static readonly JxrPixelFormat Ycc420_12Bpp         = Std(0x44);
    public static readonly JxrPixelFormat Ycc422_16Bpp         = Std(0x45);
    public static readonly JxrPixelFormat Ycc422_20Bpp         = Std(0x46);
    public static readonly JxrPixelFormat Ycc422_32Bpp         = Std(0x47);
    public static readonly JxrPixelFormat Ycc444_24Bpp         = Std(0x48);
    public static readonly JxrPixelFormat Ycc444_30Bpp         = Std(0x49);
    public static readonly JxrPixelFormat Ycc444_48Bpp         = Std(0x4A);
    public static readonly JxrPixelFormat Ycc444FixedPoint48Bpp = Std(0x4B);
    public static readonly JxrPixelFormat Ycc420Alpha20Bpp     = Std(0x4C);
    public static readonly JxrPixelFormat Ycc422Alpha24Bpp     = Std(0x4D);
    public static readonly JxrPixelFormat Ycc422Alpha30Bpp     = Std(0x4E);
    public static readonly JxrPixelFormat Ycc422Alpha48Bpp     = Std(0x4F);
    public static readonly JxrPixelFormat Ycc444Alpha32Bpp     = Std(0x50);
    public static readonly JxrPixelFormat Ycc444Alpha40Bpp     = Std(0x51);
    public static readonly JxrPixelFormat Ycc444Alpha64Bpp     = Std(0x52);
    public static readonly JxrPixelFormat Ycc444AlphaFixedPoint64Bpp = Std(0x53);
    public static readonly JxrPixelFormat CmykDirect32Bpp      = Std(0x54);
    public static readonly JxrPixelFormat CmykDirect64Bpp      = Std(0x55);
    public static readonly JxrPixelFormat CmykDirectAlpha40Bpp = Std(0x56);
}
