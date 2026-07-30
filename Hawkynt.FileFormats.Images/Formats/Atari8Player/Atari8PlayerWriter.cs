using System;

namespace FileFormat.Atari8Player;

/// <summary>Assembles AtariTools-800 player bytes.</summary>
public static class Atari8PlayerWriter {

  public static byte[] ToBytes(Atari8PlayerFile file) {
    var result = new byte[Atari8PlayerFile.FileSize];
    result[0] = file.Color;

    var shape = file.Shape ?? [];
    shape.AsSpan(0, Math.Min(shape.Length, Atari8PlayerFile.Height)).CopyTo(result.AsSpan(Atari8PlayerFile.ShapeOffset));

    return result;
  }
}
