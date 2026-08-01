using System;

namespace FileFormat.SketchPaddles;

/// <summary>Assembles a Sketch-PadDles picture from a <see cref="SketchPaddlesFile"/>.</summary>
public static class SketchPaddlesWriter {

  public static byte[] ToBytes(SketchPaddlesFile file) {
    var data = file.Data ?? [];
    var result = new byte[SketchPaddlesFile.FileSize];
    data.AsSpan(0, Math.Min(data.Length, result.Length)).CopyTo(result);

    return result;
  }
}
