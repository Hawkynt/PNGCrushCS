using System;

namespace FileFormat.Wzl;

/// <summary>Puts the bitmap back out of reach: the first 256 bytes exclusive-ored, the rest as it is.</summary>
public static class WzlWriter {

  public static byte[] ToBytes(WzlFile file) {
    var bitmap = file.Bitmap ?? [];
    if (bitmap.Length < WzlFile.MinimumLength)
      throw new ArgumentException("A .wzl picture needs a bitmap to scramble.", nameof(file));

    var result = (byte[])bitmap.Clone();
    var scrambled = Math.Min(WzlFile.ScrambledLength, result.Length);
    for (var at = 0; at < scrambled; ++at)
      result[at] ^= WzlFile.Key;

    return result;
  }
}
