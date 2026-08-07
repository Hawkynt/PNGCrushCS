using System;

namespace FileFormat.MgrBitmap;

/// <summary>Assembles MGR bitmap file bytes from a <see cref="MgrBitmapFile"/>.</summary>
/// <remarks>
/// Eight bytes of header: the two letters, then the width, height and depth as six-bit pairs biased
/// into printable range. This used to write the dimensions as the text "800x600" followed by a
/// newline, which is not a form the format has.
/// </remarks>
public static class MgrBitmapWriter {

  public static byte[] ToBytes(MgrBitmapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = file.PixelData ?? [];
    var result = new byte[MgrBitmapFile.HeaderSize + pixels.Length];
    result[0] = (byte)'y';
    result[1] = (byte)'z';
    _WritePair(result, 2, file.Width);
    _WritePair(result, 4, file.Height);
    result[6] = MgrBitmapFile.HeaderBias + 1;
    result[7] = MgrBitmapFile.HeaderBias;

    pixels.CopyTo(result.AsSpan(MgrBitmapFile.HeaderSize));

    return result;
  }

  private static void _WritePair(Span<byte> target, int at, int value) {
    target[at] = (byte)(MgrBitmapFile.HeaderBias + ((value >> 6) & 0x3F));
    target[at + 1] = (byte)(MgrBitmapFile.HeaderBias + (value & 0x3F));
  }
}
