using System;

namespace FileFormat.ZeissBivas;

/// <summary>Assembles Zeiss BIVAS microscopy bytes from a <see cref="ZeissBivasFile"/>.</summary>
public static class ZeissBivasWriter {

  public static byte[] ToBytes(ZeissBivasFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZeissBivasFile.HeaderSize + file.PixelData.Length];

    // Width (LE)
    _WriteUInt32LE(result, 0, (uint)file.Width);
    // Height (LE)
    _WriteUInt32LE(result, 4, (uint)file.Height);
    // BitsPerPixel (LE)
    _WriteUInt32LE(result, 8, (uint)file.BitsPerPixel);

    // Pixel data
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(ZeissBivasFile.HeaderSize));

    return result;
  }

  private static void _WriteUInt32LE(byte[] data, int offset, uint value) {
    data[offset] = (byte)(value & 0xFF);
    data[offset + 1] = (byte)((value >> 8) & 0xFF);
    data[offset + 2] = (byte)((value >> 16) & 0xFF);
    data[offset + 3] = (byte)(value >> 24);
  }
}
