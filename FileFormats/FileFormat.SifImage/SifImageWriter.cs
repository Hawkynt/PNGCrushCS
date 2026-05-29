using System;

namespace FileFormat.SifImage;

/// <summary>Assembles SIF image file bytes from a SifImageFile.</summary>
public static class SifImageWriter {

  public static byte[] ToBytes(SifImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[SifImageFile.HeaderSize + pixelDataSize];

    result[0] = SifImageFile.Magic[0];
    result[1] = SifImageFile.Magic[1];
    result[2] = SifImageFile.Magic[2];
    result[3] = SifImageFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(SifImageFile.HeaderSize));

    return result;
  }
}
