using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Phm;

/// <summary>Assembles PHM file bytes from half-precision pixel data.</summary>
public static class PhmWriter {

  public static byte[] ToBytes(PhmFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.ColorMode,
    file.Scale,
    file.IsLittleEndian
  );

  internal static byte[] Assemble(
    Half[] pixelData,
    int width,
    int height,
    PhmColorMode colorMode,
    float scale = 1.0f,
    bool isLittleEndian = true
  ) {
    using var ms = new MemoryStream();

    var magic = colorMode == PhmColorMode.Rgb ? "PH" : "Ph";
    ms.Write(Encoding.ASCII.GetBytes(magic));
    ms.WriteByte((byte)'\n');

    var dimensions = $"{width} {height}";
    ms.Write(Encoding.ASCII.GetBytes(dimensions));
    ms.WriteByte((byte)'\n');

    var signedScale = isLittleEndian ? -Math.Abs(scale) : Math.Abs(scale);
    var scaleStr = signedScale.ToString("G", CultureInfo.InvariantCulture);
    ms.Write(Encoding.ASCII.GetBytes(scaleStr));
    ms.WriteByte((byte)'\n');

    var channelsPerPixel = colorMode == PhmColorMode.Rgb ? 3 : 1;
    var halvesPerRow = width * channelsPerPixel;
    var needSwap = isLittleEndian != BitConverter.IsLittleEndian;

    for (var row = height - 1; row >= 0; --row) {
      var srcOffset = row * halvesPerRow;
      for (var i = 0; i < halvesPerRow; ++i) {
        var bytes = BitConverter.GetBytes(pixelData[srcOffset + i]);
        if (needSwap)
          Array.Reverse(bytes);
        ms.Write(bytes);
      }
    }

    return ms.ToArray();
  }
}
