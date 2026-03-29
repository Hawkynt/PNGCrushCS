using System;

namespace FileFormat.AtariGr7;

/// <summary>Assembles Atari 8-bit Graphics Mode 7 screen dump bytes from an <see cref="AtariGr7File"/>.</summary>
public static class AtariGr7Writer {

  public static byte[] ToBytes(AtariGr7File file) {
    ArgumentNullException.ThrowIfNull(file);
    return _PackPixels(file.PixelData);
  }

  private static byte[] _PackPixels(byte[] pixelData) {
    var result = new byte[AtariGr7File.FileSize];

    for (var y = 0; y < AtariGr7File.PixelHeight; ++y)
      for (var byteCol = 0; byteCol < AtariGr7File.BytesPerRow; ++byteCol) {
        var baseX = byteCol * 4;
        var rowOffset = y * AtariGr7File.PixelWidth;
        var p0 = _GetPixel(pixelData, rowOffset + baseX);
        var p1 = _GetPixel(pixelData, rowOffset + baseX + 1);
        var p2 = _GetPixel(pixelData, rowOffset + baseX + 2);
        var p3 = _GetPixel(pixelData, rowOffset + baseX + 3);
        result[y * AtariGr7File.BytesPerRow + byteCol] = (byte)((p0 << 6) | (p1 << 4) | (p2 << 2) | p3);
      }

    return result;
  }

  private static int _GetPixel(byte[] pixelData, int index) =>
    index < pixelData.Length ? pixelData[index] & 0x03 : 0;
}
