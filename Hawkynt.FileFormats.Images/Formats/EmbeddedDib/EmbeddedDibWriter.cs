using System;
using FileFormat.Core;
using FileFormat.Wrappers;

namespace FileFormat.EmbeddedDib;

/// <summary>Writes a packed Windows DIB: information header, optional masks/palette and pixel rows.</summary>
/// <remarks>
/// A packed DIB deliberately omits the 14-byte <c>BITMAPFILEHEADER</c> used by standalone <c>.bmp</c>
/// files. That makes this both the writer for the standalone <c>.dib</c> format and the low-level
/// payload writer used by containers that embed a Windows bitmap.
/// </remarks>
public static class EmbeddedDibWriter {

  /// <summary>Serializes the bitmap carried by <paramref name="file"/> as a packed DIB.</summary>
  public static byte[] ToBytes(EmbeddedDibFile file)
    => ToBytes(file.Preview ?? throw new ArgumentException("No bitmap is available to write.", nameof(file)));

  /// <summary>Serializes <paramref name="image"/> as a packed DIB.</summary>
  public static byte[] ToBytes(RawImage image)
    => WrappedDib.Encode(image);
}
