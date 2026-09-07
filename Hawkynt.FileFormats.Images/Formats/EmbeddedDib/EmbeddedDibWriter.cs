using System;
using FileFormat.Core;
using FileFormat.Wrappers;

namespace FileFormat.EmbeddedDib;

/// <summary>Writes the packed Windows DIB payload embedded in drawing and project containers.</summary>
/// <remarks>
/// This writes only the <c>BITMAPINFOHEADER</c>, optional palette and pixel rows. It deliberately
/// omits the 14-byte <c>BITMAPFILEHEADER</c> used by standalone BMP files because the surrounding
/// container supplies its own framing.
/// <para/>
/// It is intentionally not the registry writer for <see cref="EmbeddedDibFile"/>: the extensions
/// registered there name drawing and project formats, and a packed DIB by itself is not a valid
/// file in any of those formats. Their writers can use this payload when constructing the container.
/// </remarks>
public static class EmbeddedDibWriter {

  /// <summary>Serializes the preview carried by <paramref name="file"/> as a packed DIB payload.</summary>
  public static byte[] ToBytes(EmbeddedDibFile file)
    => ToBytes(file.Preview ?? throw new ArgumentException("No preview is available to write.", nameof(file)));

  /// <summary>Serializes <paramref name="image"/> as a packed DIB payload.</summary>
  public static byte[] ToBytes(RawImage image)
    => WrappedDib.Encode(image);
}
