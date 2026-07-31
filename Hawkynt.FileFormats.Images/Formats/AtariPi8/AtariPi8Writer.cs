using System;

namespace FileFormat.AtariPi8;

/// <summary>Assembles PI8 picture bytes from an <see cref="AtariPi8File"/>.</summary>
public static class AtariPi8Writer {

  /// <summary>Writes the monochrome form, whose length is what tells it from the colour one.</summary>
  /// <remarks>
  /// Nothing in either form says which it is: a Graphics 15 screen is 7680 bytes and a Graphics 8
  /// screen 7685, and that is the whole of the distinction. The five extra bytes are not part of
  /// the picture.
  /// </remarks>
  public static byte[] ToBytes(AtariPi8File file) {
    var data = new byte[AtariPi8File.MonochromeSize];
    var stored = file.Data ?? [];
    var from = file.BitmapOffset;
    var length = Math.Min(AtariPi8File.ColorSize, Math.Max(0, stored.Length - from));

    if (length > 0)
      stored.AsSpan(from, length).CopyTo(data);

    return data;
  }
}
