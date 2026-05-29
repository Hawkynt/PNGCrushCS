using System;

namespace FileFormat.PlotMaker;

/// <summary>Assembles Plot Maker file bytes from a PlotMakerFile.</summary>
public static class PlotMakerWriter {

  public static byte[] ToBytes(PlotMakerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerRow = (file.Width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * file.Height;
    var fileSize = PlotMakerFile.HeaderSize + expectedPixelBytes;
    var result = new byte[fileSize];

    // Width (2 bytes, little-endian)
    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)(file.Width >> 8);

    // Height (2 bytes, little-endian)
    result[2] = (byte)(file.Height & 0xFF);
    result[3] = (byte)(file.Height >> 8);

    // Pixel data
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedPixelBytes)).CopyTo(result.AsSpan(PlotMakerFile.HeaderSize));

    return result;
  }
}
