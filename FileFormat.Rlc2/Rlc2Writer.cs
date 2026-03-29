using System;

namespace FileFormat.Rlc2;

/// <summary>Assembles RLC2 image file bytes from an Rlc2File.</summary>
public static class Rlc2Writer {

  public static byte[] ToBytes(Rlc2File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[Rlc2File.HeaderSize + pixelDataSize];

    result[0] = Rlc2File.Magic[0];
    result[1] = Rlc2File.Magic[1];
    result[2] = Rlc2File.Magic[2];
    result[3] = Rlc2File.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(Rlc2File.HeaderSize));

    return result;
  }
}
