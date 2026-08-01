namespace FileFormat.SunRaster;

/// <summary>
/// The values of a Sun Raster's type field, which says both how the pixels are encoded and, for one
/// of them, what order the channels come in.
/// </summary>
/// <remarks>
/// These used to be numbered None=0, Rle=1, Experimental=2, which is not what the format uses: 1 is
/// RT_STANDARD and means *uncompressed*, and 2 is RT_BYTE_ENCODED and means RLE. So an ordinary
/// uncompressed file was handed to the RLE decompressor and a genuinely compressed one was copied out
/// raw — each producing noise from a file the other's path would have read correctly.
/// </remarks>
public enum SunRasterCompression {
  /// <summary>RT_OLD: uncompressed, and the same layout as <see cref="None"/>.</summary>
  Old = 0,

  /// <summary>RT_STANDARD: uncompressed, channels in blue-green-red order.</summary>
  None = 1,

  /// <summary>RT_BYTE_ENCODED: run-length encoded.</summary>
  Rle = 2,

  /// <summary>RT_FORMAT_RGB: uncompressed, channels in red-green-blue order.</summary>
  Rgb = 3,

  /// <summary>RT_FORMAT_TIFF.</summary>
  Tiff = 4,

  /// <summary>RT_FORMAT_IFF.</summary>
  Iff = 5,

  /// <summary>RT_EXPERIMENTAL.</summary>
  Experimental = 0xFFFF,
}
