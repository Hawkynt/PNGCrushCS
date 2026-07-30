using System;
using System.IO;

namespace FileFormat.MadStudioMissile;

/// <summary>Writes Mad Studio missiles to bytes, streams, or file paths.</summary>
public static class MadStudioMissileWriter {

  public static byte[] ToBytes(MadStudioMissileFile file) {
    var rows = file.Rows ?? [];
    var data = new byte[MadStudioMissileFile.RowOffset + rows.Length];
    data[0] = (byte)rows.Length;
    data[1] = file.Color;
    for (var i = 0; i < rows.Length; ++i)
      data[MadStudioMissileFile.RowOffset + i] = (byte)(rows[i] & 3);

    return data;
  }

  public static void ToStream(MadStudioMissileFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var data = ToBytes(file);
    stream.Write(data, 0, data.Length);
  }

  public static void ToFile(MadStudioMissileFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
