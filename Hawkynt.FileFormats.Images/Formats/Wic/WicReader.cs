using System;
using System.IO;

namespace FileFormat.Wic;

/// <summary>Reads JWIC files of either header generation.</summary>
internal static class WicReader {

  public static WicFile FromSpan(ReadOnlySpan<byte> data) {
    var header = WicHeader.Parse(data);
    if (data.Length < WicHeader.Size + 2)
      throw new InvalidDataException("JWIC file has a header but no coder stream.");

    var bytes = data.ToArray();
    var pixels = WicCodec.Decode(header, bytes);
    var matrixArea = (double)header.MatrixWidth * header.MatrixHeight;

    return new() {
      Width = header.Width,
      Height = header.Height,
      Generation = header.Generation,
      Transform = header.Transform,
      IsGrayscale = !header.IsColour,
      Dpi = header.Dpi,
      ChromaBudgetScale = (int)Math.Min(header.ChromaBudgetScale, 128u),
      BitsPerPixelBudget = (float)((data.Length - WicHeader.Size) * 8 / matrixArea),
      PixelData = pixels,
    };
  }
}
