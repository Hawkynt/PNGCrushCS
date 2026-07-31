using System;

namespace FileFormat.AtariPi9;

/// <summary>Assembles Graphics 9 picture bytes from an <see cref="AtariPi9File"/>.</summary>
public static class AtariPi9Writer {

  /// <summary>Bytes past the screen that the format allows and the picture does not use.</summary>
  private const int _TRAILER = 4;

  /// <summary>Writes the plain Graphics 9 form, which is the screen and nothing else.</summary>
  public static byte[] ToBytes(AtariPi9File file) {
    var data = new byte[AtariPi9File.Gr9Size + _TRAILER];
    var stored = file.Data ?? [];
    var from = file.BitmapOffset;
    var length = Math.Min(AtariPi9File.Gr9Size, Math.Max(0, stored.Length - from));

    if (length > 0)
      stored.AsSpan(from, length).CopyTo(data);

    return data;
  }
}
