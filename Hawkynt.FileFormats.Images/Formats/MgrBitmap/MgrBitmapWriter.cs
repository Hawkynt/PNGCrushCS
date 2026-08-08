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
    var header = file.HasDepthByte ? MgrBitmapFile.HeaderSize : MgrBitmapFile.ShortHeaderSize;
    var result = new byte[header + pixels.Length];

    // The shorter form and its letters, which is what the one real sample is. A file read in the
    // longer form is written back in it, so its length does not change under a round trip.
    result[0] = (byte)'z';
    result[1] = (byte)'z';
    _WritePair(result, 2, file.Width);
    _WritePair(result, 4, file.Height);

    if (file.HasDepthByte) {
      result[6] = MgrBitmapFile.HeaderBias + 1;
      result[7] = MgrBitmapFile.HeaderBias;
    }

    pixels.CopyTo(result.AsSpan(header));

    return result;
  }

  private static void _WritePair(Span<byte> target, int at, int value) {
    target[at] = (byte)(MgrBitmapFile.HeaderBias + ((value >> 6) & 0x3F));
    target[at + 1] = (byte)(MgrBitmapFile.HeaderBias + (value & 0x3F));
  }
}
