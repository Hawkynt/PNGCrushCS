using System;

namespace FileFormat.SegaSj1;

/// <summary>Assembles Sega SJ1 file bytes from a SegaSj1File.</summary>
public static class SegaSj1Writer {

  public static byte[] ToBytes(SegaSj1File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[SegaSj1File.HeaderSize + pixelDataSize];

    result[0] = SegaSj1File.Magic[0];
    result[1] = SegaSj1File.Magic[1];
    result[2] = SegaSj1File.Magic[2];
    result[3] = SegaSj1File.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(SegaSj1File.HeaderSize));

    return result;
  }
}
