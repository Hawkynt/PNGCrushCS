using System;

namespace FileFormat.Atari8Bit;

/// <summary>Assembles Atari 8-bit screen dump bytes from an <see cref="Atari8BitFile"/>.</summary>
public static class Atari8BitWriter {

  public static byte[] ToBytes(Atari8BitFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _PackPixels(file.PixelData, file.Width, file.Height, file.Mode);
  }

  private static byte[] _PackPixels(byte[] pixelData, int width, int height, Atari8BitMode mode) {
    var bpp = Atari8BitFile.GetBitsPerPixel(mode);
    var pixelsPerByte = 8 / bpp;
    var bytesPerRow = Atari8BitFile.GetBytesPerRow(mode);
    var pixelScale = Atari8BitFile.GetPixelScale(mode);
    var fileSize = Atari8BitFile.GetFileSize(mode);
    var result = new byte[fileSize];
    var mask = (1 << bpp) - 1;

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < bytesPerRow; ++byteCol) {
        byte packed = 0;
        for (var p = 0; p < pixelsPerByte; ++p) {
          var x = (byteCol * pixelsPerByte + p) * pixelScale;
          byte value = 0;
          if (x < width) {
            var pixelIndex = y * width + x;
            if (pixelIndex < pixelData.Length)
              value = (byte)(pixelData[pixelIndex] & mask);
          }
          var shift = (pixelsPerByte - 1 - p) * bpp;
          packed |= (byte)(value << shift);
        }
        var offset = y * bytesPerRow + byteCol;
        if (offset < fileSize)
          result[offset] = packed;
      }

    return result;
  }
}
