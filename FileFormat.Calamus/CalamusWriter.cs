using System;

namespace FileFormat.Calamus;

/// <summary>Assembles Calamus raster image file bytes from a CalamusFile.</summary>
public static class CalamusWriter {

  public static byte[] ToBytes(CalamusFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[CalamusFile.HeaderSize + pixelDataSize];

    result[0] = CalamusFile.Magic[0];
    result[1] = CalamusFile.Magic[1];
    result[2] = CalamusFile.Magic[2];
    result[3] = CalamusFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Bpp);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(CalamusFile.HeaderSize));

    return result;
  }
}
