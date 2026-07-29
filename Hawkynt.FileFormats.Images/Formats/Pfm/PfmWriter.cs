using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Pfm;

/// <summary>Assembles PFM file bytes from pixel data.</summary>
public static class PfmWriter {

  public static byte[] ToBytes(PfmFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.ColorMode,
    file.Scale,
    file.IsLittleEndian
  );

  internal static byte[] Assemble(
    float[] pixelData,
    int width,
    int height,
    PfmColorMode colorMode,
    float scale = 1.0f,
    bool isLittleEndian = true
  ) {
    using var ms = new MemoryStream();

    // Line 1: magic
    var magic = colorMode == PfmColorMode.Rgb ? "PF" : "Pf";
    ms.Write(Encoding.ASCII.GetBytes(magic));
    ms.WriteByte((byte)'\n');

    // Line 2: dimensions
    var dimensions = $"{width} {height}";
    ms.Write(Encoding.ASCII.GetBytes(dimensions));
    ms.WriteByte((byte)'\n');

    // Line 3: scale factor with sign indicating endianness
    var signedScale = isLittleEndian ? -Math.Abs(scale) : Math.Abs(scale);
    var scaleStr = signedScale.ToString("G", CultureInfo.InvariantCulture);
    ms.Write(Encoding.ASCII.GetBytes(scaleStr));
    ms.WriteByte((byte)'\n');

    // Pixel data — write bottom-to-top (PFM storage order)
    var channelsPerPixel = colorMode == PfmColorMode.Rgb ? 3 : 1;
    var floatsPerRow = width * channelsPerPixel;
    var needSwap = isLittleEndian != BitConverter.IsLittleEndian;

    for (var row = height - 1; row >= 0; --row) {
      var srcOffset = row * floatsPerRow;
      for (var i = 0; i < floatsPerRow; ++i) {
        var bytes = BitConverter.GetBytes(pixelData[srcOffset + i]);
        if (needSwap)
          Array.Reverse(bytes);
        ms.Write(bytes);
      }
    }

    return ms.ToArray();
  }
}
