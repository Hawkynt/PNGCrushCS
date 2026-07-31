using System;
using FileFormat.Core;

namespace FileFormat.ZxTrefiBorderScreen;

/// <summary>Assembles Border Screen bytes from a <see cref="ZxTrefiBorderScreenFile"/>.</summary>
public static class ZxTrefiBorderScreenWriter {

  /// <summary>
  /// Writes the plain form: one screen, no border, which is the only one that needs no decisions.
  /// </summary>
  /// <remarks>
  /// The bordered form would need the colour runs timed as the beam travels, and the two-field
  /// forms would need a picture split into two that average into it — both are choices about what
  /// to trade, and neither has an answer that holds for every picture.
  /// </remarks>
  public static byte[] ToBytes(ZxTrefiBorderScreenFile file) {
    var data = new byte[ZxTrefiBorderScreenFile.FirstBitmapOffset + ZxTrefiBorderScreenFile.ScreenSize];
    var screen = file.Data ?? [];

    // The flag byte says a single screen with no border; the rest of the header is the program's
    // own and means nothing to the picture.
    data[3] = 0;

    var from = file.Fields is { Length: > 0 } ? file.Fields[0].Bitmap : ZxTrefiBorderScreenFile.FirstBitmapOffset;
    if (from + ZxTrefiBorderScreenFile.ScreenSize <= screen.Length)
      screen.AsSpan(from, ZxTrefiBorderScreenFile.ScreenSize)
        .CopyTo(data.AsSpan(ZxTrefiBorderScreenFile.FirstBitmapOffset));

    return data;
  }
}
