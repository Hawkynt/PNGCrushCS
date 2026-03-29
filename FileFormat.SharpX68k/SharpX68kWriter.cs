using System;

namespace FileFormat.SharpX68k;

/// <summary>Assembles sharp x68000 16-bit color screen bytes from pixel data.</summary>
public static class SharpX68kWriter {

  public static byte[] ToBytes(SharpX68kFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var result = new byte[SharpX68kFile.HeaderSize + width * height * 2];
    result[0] = (byte)(width & 0xFF);
    result[1] = (byte)(width >> 8);
    result[2] = (byte)(height & 0xFF);
    result[3] = (byte)(height >> 8);

    var pixelCount = width * height;
    for (var i = 0; i < pixelCount; ++i) {
      var r = (pixelData[i * 3] >> 3) & 0x1F;
      var g = (pixelData[i * 3 + 1] >> 3) & 0x1F;
      var b = (pixelData[i * 3 + 2] >> 3) & 0x1F;
      var rgb555 = (ushort)((r << 10) | (g << 5) | b);
      result[SharpX68kFile.HeaderSize + i * 2] = (byte)(rgb555 & 0xFF);
      result[SharpX68kFile.HeaderSize + i * 2 + 1] = (byte)(rgb555 >> 8);
    }

    return result;
  }
}
