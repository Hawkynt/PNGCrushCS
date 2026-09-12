namespace FileFormat.Gif;

/// <summary>The Logical Screen Descriptor's colour-resolution field, as the raw 3-bit value stored on
/// disk: <c>e</c> such that the source image had <c>2^(e+1)</c> distinguishable levels per primary.</summary>
/// <remarks>
/// The field is informational — a decoder is not required to act on it, and virtually every modern
/// writer emits <see cref="Colored256"/> regardless of how large the colour table actually is.
/// <see cref="GifLogicalScreenDescriptor.ColorResolution"/> stores the same quantity as a bit
/// <em>count</em> (1..8); <see cref="GifLogicalScreenDescriptor.ColorResolutionValue"/> converts.
/// </remarks>
public enum ColorResolution : byte {
  /// <summary>One bit per primary — two levels.</summary>
  Monochrome = 0,
  /// <summary>Two bits per primary — four levels.</summary>
  Colored4 = 1,
  /// <summary>Three bits per primary — eight levels.</summary>
  Colored8 = 2,
  /// <summary>Four bits per primary — sixteen levels.</summary>
  Colored16 = 3,
  /// <summary>Five bits per primary — thirty-two levels.</summary>
  Colored32 = 4,
  /// <summary>Six bits per primary — sixty-four levels.</summary>
  Colored64 = 5,
  /// <summary>Seven bits per primary — one hundred and twenty-eight levels.</summary>
  Colored128 = 6,
  /// <summary>Eight bits per primary — the full 256 levels. What almost every real GIF carries.</summary>
  Colored256 = 7,
}
