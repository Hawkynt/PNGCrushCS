using System;
using FileFormat.Core;

namespace FileFormat.AtariGr7;

/// <summary>Assembles Atari 8-bit Graphics Mode 7 screen dump bytes from an <see cref="AtariGr7File"/>.</summary>
public static class AtariGr7Writer {

  public static byte[] ToBytes(AtariGr7File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = _PackPixels(file.PixelData);

    // The four registers the rows are drawn from, after them: background first, then the three
    // playfields, which is the order pixel values 0 to 3 select.
    var palette = file.Palette ?? [];
    for (var i = 0; i < AtariGr7File.RegisterCount; ++i) {
      var entry = i * 3;
      result[AtariGr7File.BytesPerRow * AtariGr7File.PixelHeight + i] = entry + 2 < palette.Length
        ? Atari8BitGraphics.NearestRegister(palette[entry], palette[entry + 1], palette[entry + 2])
        : (byte)0;
    }

    return result;
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
