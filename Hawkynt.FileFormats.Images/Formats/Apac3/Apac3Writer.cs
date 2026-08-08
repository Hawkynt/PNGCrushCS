using System;

namespace FileFormat.Apac3;

/// <summary>Assembles an APAC 3 picture from an <see cref="Apac3File"/>.</summary>
public static class Apac3Writer {

  /// <summary>
  /// Writes the file, whose length is its only header — it is what says where the hue halves begin.
  /// </summary>
  public static byte[] ToBytes(Apac3File file) {
    var source = file.Data ?? [];
    var length = file.HueOffset == Apac3File.CompactHueOffset ? Apac3File.CompactSize : source.Length;
    var data = new byte[length];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);

    return data;
  }
}
