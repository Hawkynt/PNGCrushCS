using System;
using System.IO;

namespace FileFormat.AmstradMode5;

/// <summary>Assembles a Mode 5 colour file. The bitmap goes beside it, not in it.</summary>
public static class AmstradMode5Writer {

  public static byte[] ToBytes(AmstradMode5File file) {
    ArgumentNullException.ThrowIfNull(file);

    var colors = file.Colors ?? new byte[AmstradMode5File.FileSize];
    var result = new byte[AmstradMode5File.FileSize];
    colors.AsSpan(0, Math.Min(colors.Length, AmstradMode5File.FileSize)).CopyTo(result);

    return result;
  }

  public static void ToFile(AmstradMode5File file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
