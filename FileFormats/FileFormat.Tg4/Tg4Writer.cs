using System;

namespace FileFormat.Tg4;

/// <summary>Assembles TG4 file bytes from a Tg4File.</summary>
public static class Tg4Writer {

  public static byte[] ToBytes(Tg4File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[Tg4File.HeaderSize + pixelDataSize];

    result[0] = Tg4File.Magic[0];
    result[1] = Tg4File.Magic[1];
    result[2] = Tg4File.Magic[2];
    result[3] = Tg4File.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(Tg4File.HeaderSize));

    return result;
  }
}
