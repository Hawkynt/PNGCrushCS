using System;

namespace FileFormat.Pcd;

/// <summary>Assembles PCD (Kodak Photo CD) file bytes from pixel data.</summary>
/// <remarks>
/// Photo CD is a fixed-resolution format, so the only thing that can be written is a Base image at
/// 768x512. What stood here wrote the dimensions into two bytes after the magic and then interleaved
/// RGB, which is not Photo CD at all — no other reader would have opened the result, and this
/// library's own reader only did because it was wrong in the same way.
/// </remarks>
public static class PcdWriter {

  public static byte[] ToBytes(PcdFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width != PcdReader.BaseWidth || height != PcdReader.BaseHeight)
      throw new NotSupportedException(
        $"Photo CD holds fixed resolutions; only a {PcdReader.BaseWidth}x{PcdReader.BaseHeight} Base "
        + $"image can be written, not {width}x{height}.");

    const int chromaWidth = PcdReader.BaseWidth / 2;
    const int groupSize = (PcdReader.BaseWidth * 2) + (chromaWidth * 2);
    var result = new byte[PcdReader.BaseImageOffset + (groupSize * (PcdReader.BaseHeight / 2))];

    PcdFile.Magic.AsSpan().CopyTo(result.AsSpan(PcdFile.PreambleSize));

    for (var y = 0; y < height; ++y) {
      var group = PcdReader.BaseImageOffset + ((y / 2) * groupSize);
      var lumaRow = group + ((y % 2) * width);
      var cbRow = group + (width * 2);
      var crRow = cbRow + chromaWidth;

      for (var x = 0; x < width; ++x) {
        var at = ((y * width) + x) * 3;
        var r = at < pixelData.Length ? pixelData[at] : 0;
        var g = at + 1 < pixelData.Length ? pixelData[at + 1] : 0;
        var b = at + 2 < pixelData.Length ? pixelData[at + 2] : 0;

        var luma = _Clamp((0.299 * r) + (0.587 * g) + (0.114 * b));
        result[lumaRow + x] = luma;

        // The inverse of the reader's transform. Chroma is taken from every second pixel of every
        // second row, which is exactly where the reader samples it.
        if ((x & 1) == 0 && (y & 1) == 0) {
          result[cbRow + (x / 2)] = _Clamp(156 + ((b - luma) / 2.2179));
          result[crRow + (x / 2)] = _Clamp(137 + ((r - luma) / 1.8215));
        }
      }
    }

    return result;
  }

  private static byte _Clamp(double value)
    => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)Math.Round(value);
}
