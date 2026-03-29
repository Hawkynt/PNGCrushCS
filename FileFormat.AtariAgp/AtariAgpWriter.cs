using System;

namespace FileFormat.AtariAgp;

/// <summary>Assembles Atari 8-bit AGP image bytes from an <see cref="AtariAgpFile"/>.</summary>
public static class AtariAgpWriter {

  public static byte[] ToBytes(AtariAgpFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Mode switch {
      AtariAgpMode.Graphics7 => _PackGr7(file),
      AtariAgpMode.Graphics8WithColors => _PackGr8WithColors(file),
      _ => _PackGr8(file),
    };
  }

  private static byte[] _PackGr8(AtariAgpFile file) {
    var result = new byte[AtariAgpFile.FileSizeGr8];
    _PackGr8Pixels(file.PixelData, file.Width, file.Height, result);
    return result;
  }

  private static byte[] _PackGr8WithColors(AtariAgpFile file) {
    var result = new byte[AtariAgpFile.FileSizeGr8WithColors];
    _PackGr8Pixels(file.PixelData, file.Width, file.Height, result);
    result[AtariAgpFile.FileSizeGr8] = file.BackgroundColor;
    result[AtariAgpFile.FileSizeGr8 + 1] = file.ForegroundColor;
    return result;
  }

  private static void _PackGr8Pixels(byte[] pixelData, int width, int height, byte[] result) {
    var bytesPerRow = width / 8;
    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < bytesPerRow; ++byteCol) {
        byte packed = 0;
        var baseX = byteCol * 8;
        for (var bit = 0; bit < 8; ++bit) {
          var x = baseX + bit;
          byte value = 0;
          if (x < width) {
            var idx = y * width + x;
            if (idx < pixelData.Length)
              value = (byte)(pixelData[idx] & 1);
          }
          packed |= (byte)(value << (7 - bit));
        }
        result[y * bytesPerRow + byteCol] = packed;
      }
  }

  private static byte[] _PackGr7(AtariAgpFile file) {
    var result = new byte[AtariAgpFile.FileSizeGr7];
    var bytesPerRow = 40;

    for (var y = 0; y < file.Height; ++y)
      for (var byteCol = 0; byteCol < bytesPerRow; ++byteCol) {
        var baseX = byteCol * 4;
        var rowOffset = y * file.Width;
        var p0 = _GetPixel2bpp(file.PixelData, rowOffset + baseX);
        var p1 = _GetPixel2bpp(file.PixelData, rowOffset + baseX + 1);
        var p2 = _GetPixel2bpp(file.PixelData, rowOffset + baseX + 2);
        var p3 = _GetPixel2bpp(file.PixelData, rowOffset + baseX + 3);
        result[y * bytesPerRow + byteCol] = (byte)((p0 << 6) | (p1 << 4) | (p2 << 2) | p3);
      }

    return result;
  }

  private static int _GetPixel2bpp(byte[] pixelData, int index) =>
    index < pixelData.Length ? pixelData[index] & 0x03 : 0;
}
