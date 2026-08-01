namespace FileFormat.SunRaster;

/// <summary>What the <c>Type</c> field in a Sun Raster header says the pixels are.</summary>
/// <remarks>
/// This one field carries two unrelated facts — whether the rows are run-length encoded, and which
/// way round the colour channels sit — because the format grew them one at a time. Treating it as a
/// compression setting alone loses the second, which is how a picture comes out with its red and
/// blue exchanged while nothing appears to be wrong.
/// </remarks>
public enum SunRasterType {

  /// <summary>The original format, with no length field. Blue first.</summary>
  Old = 0,

  /// <summary>Uncompressed rows, blue first, which is what most files are.</summary>
  Standard = 1,

  /// <summary>Run-length encoded rows, blue first.</summary>
  ByteEncoded = 2,

  /// <summary>Uncompressed rows with the channels the other way round: red first.</summary>
  FormatRgb = 3,

  /// <summary>The pixels are a TIFF file rather than rows.</summary>
  FormatTiff = 4,

  /// <summary>The pixels are an IFF file rather than rows.</summary>
  FormatIff = 5,

  /// <summary>Reserved for whatever a site wanted; nothing can be assumed about it.</summary>
  Experimental = 0xFFFF,
}
