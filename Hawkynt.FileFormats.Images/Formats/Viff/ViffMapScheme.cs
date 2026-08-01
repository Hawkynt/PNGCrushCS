namespace FileFormat.Viff;

/// <summary>How a VIFF colour map is applied to the bands (VFF_MS_*).</summary>
/// <remarks>
/// This, not <c>map_enable</c>, is what says whether the pixels are palette indices: ImageMagick
/// leaves <c>map_enable</c> at VFF_MAP_OPTIONAL on every file it writes, mapped or not.
/// </remarks>
public enum ViffMapScheme : uint {
  /// <summary>The pixels are the values; there is no map.</summary>
  None = 0,
  /// <summary>One map per band, which is how a paletted image is stored.</summary>
  OnePerBand = 1,
  Cycle = 2,
  Shared = 3,
  Group = 4
}
