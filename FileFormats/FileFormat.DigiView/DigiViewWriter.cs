using System;

namespace FileFormat.DigiView;

/// <summary>Assembles DigiView digitizer file bytes from a DigiViewFile.</summary>
public static class DigiViewWriter {

  public static byte[] ToBytes(DigiViewFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.Width * file.Height * file.Channels;
    var fileSize = DigiViewFile.HeaderSize + pixelDataSize;
    var result = new byte[fileSize];

    // Width (2 bytes, big-endian)
    result[0] = (byte)(file.Width >> 8);
    result[1] = (byte)(file.Width & 0xFF);

    // Height (2 bytes, big-endian)
    result[2] = (byte)(file.Height >> 8);
    result[3] = (byte)(file.Height & 0xFF);

    // Channels (1 byte)
    result[4] = file.Channels;

    // Pixel data
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelDataSize)).CopyTo(result.AsSpan(DigiViewFile.HeaderSize));

    return result;
  }
}
