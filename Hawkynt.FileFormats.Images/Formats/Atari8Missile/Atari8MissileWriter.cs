using System;
using System.IO;

namespace FileFormat.Atari8Missile;

/// <summary>Writes AtariTools-800 missiles to bytes, streams, or file paths.</summary>
public static class Atari8MissileWriter {

  public static byte[] ToBytes(Atari8MissileFile file) {
    var shape = file.Shape ?? [];
    var data = new byte[file.IsPadded ? Atari8MissileFile.PaddedFileSize : Atari8MissileFile.FileSize];
    data[0] = file.Color;
    shape.AsSpan(0, Math.Min(shape.Length, data.Length - Atari8MissileFile.ShapeOffset))
      .CopyTo(data.AsSpan(Atari8MissileFile.ShapeOffset));

    return data;
  }

  public static void ToStream(Atari8MissileFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var data = ToBytes(file);
    stream.Write(data, 0, data.Length);
  }

  public static void ToFile(Atari8MissileFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
