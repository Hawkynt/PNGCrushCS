using System;
using System.Text;

namespace FileFormat.ScitexCt;

/// <summary>Assembles Scitex CT file bytes from pixel data.</summary>
public static class ScitexCtWriter {

  public static byte[] ToBytes(ScitexCtFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.ColorMode, file.BitsPerComponent, file.HResolution, file.VResolution, file.Description);
  }

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    ScitexCtColorMode colorMode,
    int bitsPerComponent,
    int hResolution,
    int vResolution,
    string description
  ) {
    var channels = colorMode switch {
      ScitexCtColorMode.Grayscale => 1,
      ScitexCtColorMode.Rgb => 3,
      ScitexCtColorMode.Cmyk => 4,
      _ => throw new ArgumentOutOfRangeException(nameof(colorMode), colorMode, "Unknown color mode.")
    };

    var expectedPixelBytes = width * height * channels;
    var fileSize = ScitexCtHeader.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new ScitexCtHeader(
      width,
      height,
      colorMode,
      bitsPerComponent,
      0,
      hResolution,
      vResolution,
      description
    );
    header.WriteTo(span);
    Encoding.ASCII.GetBytes("CT").CopyTo(span);
    Encoding.ASCII.GetBytes(ScitexCtHeader.StructSize.ToString("D6")).CopyTo(span[2..]);

    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(ScitexCtHeader.StructSize));

    return result;
  }
}
