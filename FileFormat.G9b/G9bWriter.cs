using System;

namespace FileFormat.G9b;

/// <summary>Assembles V9990 GFX9000 (.g9b) file bytes from a <see cref="G9bFile"/>.</summary>
public static class G9bWriter {

  public static byte[] ToBytes(G9bFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var headerSize = file.HeaderSize;
    var totalSize = headerSize + file.PixelData.Length;
    var result = new byte[totalSize];

    // Magic "G9B"
    result[0] = G9bReader.Magic[0];
    result[1] = G9bReader.Magic[1];
    result[2] = G9bReader.Magic[2];

    // Header size (2 bytes LE)
    result[3] = (byte)(headerSize & 0xFF);
    result[4] = (byte)((headerSize >> 8) & 0xFF);

    // Screen mode
    result[5] = (byte)file.ScreenMode;

    // Color mode
    result[6] = file.ColorMode;

    // Width (2 bytes LE)
    result[7] = (byte)(file.Width & 0xFF);
    result[8] = (byte)((file.Width >> 8) & 0xFF);

    // Height (2 bytes LE)
    result[9] = (byte)(file.Height & 0xFF);
    result[10] = (byte)((file.Height >> 8) & 0xFF);

    // Pixel data
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(headerSize));

    return result;
  }
}
