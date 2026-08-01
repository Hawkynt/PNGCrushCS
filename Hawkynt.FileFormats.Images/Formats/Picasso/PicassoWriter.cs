using System;
using System.IO;

namespace FileFormat.Picasso;

/// <summary>Assembles a Picasso picture. Its cell colours go beside it, not in it.</summary>
public static class PicassoWriter {

  public static byte[] ToBytes(PicassoFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var data = file.Data ?? new byte[PicassoFile.FileSize];
    var result = new byte[PicassoFile.FileSize];
    data.AsSpan(0, Math.Min(data.Length, PicassoFile.FileSize)).CopyTo(result);

    return result;
  }

  public static void ToFile(PicassoFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
