using System;

namespace FileFormat.GammaFax;

/// <summary>Assembles GammaFax GMF file bytes from a GammaFaxFile.</summary>
public static class GammaFaxWriter {

  public static byte[] ToBytes(GammaFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[GammaFaxFile.HeaderSize + pixelDataSize];

    result[0] = GammaFaxFile.Magic[0];
    result[1] = GammaFaxFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Compression);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(GammaFaxFile.HeaderSize));

    return result;
  }
}
