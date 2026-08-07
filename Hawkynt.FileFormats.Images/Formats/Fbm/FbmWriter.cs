using System;

namespace FileFormat.Fbm;

/// <summary>Assembles CMU Fuzzy Bitmap (FBM) file bytes from pixel data.</summary>
public static class FbmWriter {

  public static byte[] ToBytes(FbmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Bands, file.Title);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int bands, string title) {
    // One row of ONE plane, padded, and a plane is that times the height. The bands go one whole
    // plane after another rather than interleaved, and the rows run bottom to top.
    var rowLen = (width + 15) & ~15;
    var plnLen = rowLen * height;
    const int clrLen = 0;

    var result = new byte[FbmHeader.StructSize + clrLen + plnLen * bands];

    new FbmHeader(
      Cols: width,
      Rows: height,
      Bands: bands,
      Bits: 8,
      PhysBits: 8,
      RowLen: rowLen,
      PlnLen: plnLen,
      ClrLen: clrLen,
      Aspect: 1.0,
      Title: title ?? string.Empty
    ).WriteTo(result);

    for (var band = 0; band < bands; ++band)
    for (var y = 0; y < height; ++y) {
      var target = FbmHeader.StructSize + band * plnLen + (height - 1 - y) * rowLen;
      for (var x = 0; x < width; ++x) {
        var source = (y * width + x) * bands + band;
        if (source < pixelData.Length)
          result[target + x] = pixelData[source];
      }
    }

    return result;
  }
}
