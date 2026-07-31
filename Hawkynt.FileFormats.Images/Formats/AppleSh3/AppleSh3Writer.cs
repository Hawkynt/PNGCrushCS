using System;

namespace FileFormat.AppleSh3;

/// <summary>Assembles unpacked 3200-colour picture bytes from an <see cref="AppleSh3File"/>.</summary>
public static class AppleSh3Writer {

  /// <summary>Writes the bitmap and then the two hundred palettes, which is the whole file.</summary>
  public static byte[] ToBytes(AppleSh3File file) {
    var data = new byte[AppleSh3File.FileSize];
    var stored = file.Data ?? [];
    stored.AsSpan(0, Math.Min(stored.Length, data.Length)).CopyTo(data);

    return data;
  }
}
