using System;

namespace FileFormat.Degas;

/// <summary>Assembles DEGAS/DEGAS Elite file bytes from a DegasFile.</summary>
public static class DegasWriter {

  private const int _COMPRESSION_FLAG = unchecked((short)0x8000);

/// <summary>Turns the word-interleaved screen into the plane-row-major scanlines a packed file holds.</summary>
  private static byte[] _SplitPlaneRows(byte[] data, int width, DegasResolution resolution) {
    var planes = resolution switch {
      DegasResolution.Low => 4,
      DegasResolution.Medium => 2,
      _ => 1,
    };

    if (planes == 1)
      return data;

    var wordsPerPlaneRow = (width + 15) / 16;
    var bytesPerRow = wordsPerPlaneRow * 2 * planes;
    var rows = data.Length / bytesPerRow;
    var result = new byte[data.Length];

    for (var row = 0; row < rows; ++row)
    for (var plane = 0; plane < planes; ++plane)
    for (var word = 0; word < wordsPerPlaneRow; ++word) {
      var from = row * bytesPerRow + (word * planes + plane) * 2;
      var to = row * bytesPerRow + plane * wordsPerPlaneRow * 2 + word * 2;
      result[to] = data[from];
      result[to + 1] = data[from + 1];
    }

    return result;
  }

    public static byte[] ToBytes(DegasFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var resolutionValue = (short)file.Resolution;
    if (file.IsCompressed)
      resolutionValue = (short)(resolutionValue | _COMPRESSION_FLAG);

    var header = new DegasHeader(resolutionValue, file.Palette);

    byte[] imageData;
    if (file.IsCompressed)
      // A packed picture stores each scanline as one whole plane row after another rather than the
      // machine's word-interleaved screen; the reader undoes this, and it has to be done here.
      imageData = PackBitsCompressor.Compress(_SplitPlaneRows(file.PixelData, file.Width, file.Resolution));
    else
      imageData = file.PixelData;

    var result = new byte[DegasHeader.StructSize + imageData.Length];
    header.WriteTo(result.AsSpan());
    imageData.AsSpan(0, imageData.Length).CopyTo(result.AsSpan(DegasHeader.StructSize));

    return result;
  }
}
