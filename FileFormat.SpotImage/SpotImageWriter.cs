using System;

namespace FileFormat.SpotImage;

/// <summary>Assembles SPOT satellite imagery bytes from a <see cref="SpotImageFile"/>.</summary>
public static class SpotImageWriter {

  public static byte[] ToBytes(SpotImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SpotImageFile.HeaderSize + file.PixelData.Length];

    // Magic
    SpotImageFile.Magic.AsSpan(0, SpotImageFile.Magic.Length).CopyTo(result);

    // Width (LE)
    result[4] = (byte)(file.Width & 0xFF);
    result[5] = (byte)(file.Width >> 8);

    // Height (LE)
    result[6] = (byte)(file.Height & 0xFF);
    result[7] = (byte)(file.Height >> 8);

    // BitsPerPixel (LE)
    result[8] = (byte)(file.BitsPerPixel & 0xFF);
    result[9] = (byte)(file.BitsPerPixel >> 8);

    // Reserved (6 bytes, already zero)

    // Pixel data
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(SpotImageFile.HeaderSize));

    return result;
  }
}
