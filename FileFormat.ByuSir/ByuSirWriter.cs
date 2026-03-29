using System;

namespace FileFormat.ByuSir;

/// <summary>Assembles BYU SIR file bytes from a ByuSirFile.</summary>
public static class ByuSirWriter {

  public static byte[] ToBytes(ByuSirFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[ByuSirFile.HeaderSize + pixelDataSize];

    result[0] = ByuSirFile.Magic[0];
    result[1] = ByuSirFile.Magic[1];
    result[2] = ByuSirFile.Magic[2];
    result[3] = ByuSirFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.DataType);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Reserved);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(ByuSirFile.HeaderSize));

    return result;
  }
}
