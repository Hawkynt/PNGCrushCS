using System;

namespace FileFormat.Nie;

/// <summary>Assembles NIE file bytes from pixel data.</summary>
public static class NieWriter {

  public static byte[] ToBytes(NieFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var bytesPerPixel = file.BytesPerPixel;
    var pixelDataSize = file.Width * file.Height * bytesPerPixel;
    var result = new byte[NieFile.HeaderSize + pixelDataSize];

    // Magic: nïE
    result[0] = 0x6E;
    result[1] = 0xC3;
    result[2] = 0xAF;
    result[3] = 0x45;

    // Pixel config byte + 3 padding bytes
    result[4] = (byte)file.PixelConfig;
    result[5] = 0;
    result[6] = 0;
    result[7] = 0;

    // Width and height as uint32 LE
    new NieHeader((uint)file.Width, (uint)file.Height).WriteTo(result);

    // Pixel data
    var copyLen = Math.Min(file.PixelData.Length, pixelDataSize);
    Buffer.BlockCopy(file.PixelData, 0, result, NieFile.HeaderSize, copyLen);

    return result;
  }
}
