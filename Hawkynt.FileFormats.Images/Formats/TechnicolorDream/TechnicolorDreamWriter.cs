using System;

namespace FileFormat.TechnicolorDream;

/// <summary>Assembles a Technicolor Dream luminance field from a <see cref="TechnicolorDreamFile"/>.</summary>
public static class TechnicolorDreamWriter {

  /// <summary>Writes the luminance field, which is what a .lum file is.</summary>
  /// <remarks>
  /// The hues live in a separate file of the same name, and writing one here would be writing a
  /// second file the caller did not ask for. What is written is therefore the grey half — a
  /// complete .lum, and a picture in its own right, which is how the format treats it.
  /// </remarks>
  public static byte[] ToBytes(TechnicolorDreamFile file) {
    var luminances = file.Luminances ?? [];
    var result = new byte[TechnicolorDreamFile.FileSize];
    luminances.AsSpan(0, Math.Min(luminances.Length, result.Length)).CopyTo(result);

    return result;
  }
}
