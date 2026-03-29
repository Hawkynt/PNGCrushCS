using System;

namespace FileFormat.Ps2Txc;

/// <summary>Assembles PS2 TXC texture file bytes from a Ps2TxcFile.</summary>
public static class Ps2TxcWriter {

  public static byte[] ToBytes(Ps2TxcFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[Ps2TxcFile.HeaderSize + pixelDataSize];

    BitConverter.TryWriteBytes(new Span<byte>(result, 0, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.BitsPerPixel);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(Ps2TxcFile.HeaderSize));

    return result;
  }
}
