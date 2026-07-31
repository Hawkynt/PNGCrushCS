using System;

namespace FileFormat.MsxScc;

/// <summary>Assembles MSX2+ Screen 12 bytes from an <see cref="MsxSccFile"/>.</summary>
public static class MsxSccWriter {

  /// <summary>Bytes a whole Screen 12 video memory occupies, header included.</summary>
  private const int _FULL_SIZE = 54279;

  /// <summary>
  /// Writes the whole of video memory as a BSAVE image, which is the form that needs no unpacking
  /// and carries the full 212 rows.
  /// </summary>
  /// <remarks>
  /// The packed form would save space but only on a picture with long flat runs, and a YJK screen
  /// has almost none — every group of four pixels carries its own chroma, so even a flat area
  /// varies byte to byte.
  /// </remarks>
  public static byte[] ToBytes(MsxSccFile file) {
    var screen = file.Screen ?? [];
    var data = new byte[_FULL_SIZE];

    data[0] = 254;

    // The header names the last address rather than the length, which is eight less than the size.
    var end = _FULL_SIZE - 8;
    data[3] = (byte)end;
    data[4] = (byte)(end >> 8);

    screen.AsSpan(0, Math.Min(screen.Length, _FULL_SIZE)).CopyTo(data);
    data[0] = 254;
    data[1] = 0;
    data[2] = 0;
    data[3] = (byte)end;
    data[4] = (byte)(end >> 8);
    data[5] = 0;
    data[6] = 0;

    return data;
  }
}
