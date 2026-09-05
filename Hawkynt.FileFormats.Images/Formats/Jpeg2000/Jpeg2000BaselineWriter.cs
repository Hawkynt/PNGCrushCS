using System;
using System.IO;

namespace FileFormat.Jpeg2000;

/// <summary>The JP2 authoring profile the public RawImage writer uses.</summary>
/// <remarks>
/// Lossless throughout: eight-bit unsigned samples, one tile, one quality layer, the reversible
/// colour transform for colour, the reversible 5/3 wavelet and no quantization. The only decision
/// left open is how deep to decompose, and five levels is what other encoders default to.
/// </remarks>
public static class Jpeg2000BaselineWriter {

  /// <summary>Decomposition depth used when the caller did not ask for one.</summary>
  private const int _DEFAULT_LEVELS = 5;

  public static byte[] ToBytes(Jpeg2000File file) {
    if (file.ComponentCount is not 1 and not 3)
      throw new NotSupportedException("The JPEG 2000 RawImage writer supports Gray8 and RGB24.");
    if (file.BitsPerComponent != 8)
      throw new NotSupportedException("The JPEG 2000 RawImage writer authors 8-bit components.");

    var levels = file.DecompositionLevels > 0 ? file.DecompositionLevels : _DEFAULT_LEVELS;
    return Jpeg2000Writer.ToBytes(file with { DecompositionLevels = levels });
  }
}
