using System;

namespace FileFormat.Pic2;

/// <summary>Assembles PIC2 file bytes from a Pic2File.</summary>
public static class Pic2Writer {

  public static byte[] ToBytes(Pic2File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[Pic2File.HeaderSize + pixelDataSize];

    result[0] = Pic2File.Magic[0];
    result[1] = Pic2File.Magic[1];
    result[2] = Pic2File.Magic[2];
    result[3] = Pic2File.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Bpp);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Mode);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(Pic2File.HeaderSize));

    return result;
  }
}
