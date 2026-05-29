using System;

namespace FileFormat.AtariDrg;

/// <summary>Assembles Atari 8-bit DRG graphics screen dump bytes from an <see cref="AtariDrgFile"/>.</summary>
public static class AtariDrgWriter {

  public static byte[] ToBytes(AtariDrgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _PackPixels(file.PixelData);
  }

  private static byte[] _PackPixels(byte[] pixelData) {
    var result = new byte[AtariDrgFile.FileSize];

    for (var y = 0; y < AtariDrgFile.PixelHeight; ++y)
      for (var byteCol = 0; byteCol < AtariDrgFile.BytesPerRow; ++byteCol) {
        var baseX = byteCol * 4;
        var rowOffset = y * AtariDrgFile.PixelWidth;
        var p0 = _GetPixel(pixelData, rowOffset + baseX);
        var p1 = _GetPixel(pixelData, rowOffset + baseX + 1);
        var p2 = _GetPixel(pixelData, rowOffset + baseX + 2);
        var p3 = _GetPixel(pixelData, rowOffset + baseX + 3);
        result[y * AtariDrgFile.BytesPerRow + byteCol] = (byte)((p0 << 6) | (p1 << 4) | (p2 << 2) | p3);
      }

    return result;
  }

  private static int _GetPixel(byte[] pixelData, int index) =>
    index < pixelData.Length ? pixelData[index] & 0x03 : 0;
}
