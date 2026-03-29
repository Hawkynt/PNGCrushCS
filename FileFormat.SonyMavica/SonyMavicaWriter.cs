using System;

namespace FileFormat.SonyMavica;

/// <summary>Assembles Sony Mavica .411 file bytes from a SonyMavicaFile.</summary>
public static class SonyMavicaWriter {

  public static byte[] ToBytes(SonyMavicaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[SonyMavicaFile.HeaderSize + pixelDataSize];

    result[0] = SonyMavicaFile.Magic[0];
    result[1] = SonyMavicaFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), file.Format);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(SonyMavicaFile.HeaderSize));

    return result;
  }
}
