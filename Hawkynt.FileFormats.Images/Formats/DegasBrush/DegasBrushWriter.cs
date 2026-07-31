using System;
using System.IO;

namespace FileFormat.DegasBrush;

/// <summary>Writes DEGAS Elite brushes to bytes, streams, or file paths.</summary>
public static class DegasBrushWriter {

  public static byte[] ToBytes(DegasBrushFile file) {
    var shape = file.Shape ?? [];
    var data = new byte[DegasBrushFile.FileSize];
    for (var i = 0; i < data.Length && i < shape.Length; ++i)
      data[i] = (byte)(shape[i] != 0 ? 1 : 0);

    return data;
  }

  public static void ToStream(DegasBrushFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var data = ToBytes(file);
    stream.Write(data, 0, data.Length);
  }

  public static void ToFile(DegasBrushFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
