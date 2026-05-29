using System;

namespace FileFormat.Enterprise128;

/// <summary>Assembles enterprise 128/elan screen dump bytes from pixel data.</summary>
public static class Enterprise128Writer {

  public static byte[] ToBytes(Enterprise128File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var result = new byte[Enterprise128File.FileSize];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; x += 8) {
        byte packed = 0;
        for (var bit = 0; bit < 8 && x + bit < width; ++bit)
          if (y * width + x + bit < pixelData.Length && pixelData[y * width + x + bit] != 0)
            packed |= (byte)(0x80 >> bit);
        result[y * (width / 8) + x / 8] = packed;
      }

    return result;
  }
}
