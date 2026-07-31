using System;
using System.Collections.Generic;

namespace FileFormat.MapletownNl3;

/// <summary>Assembles Mapletown Network NL3 picture bytes from a <see cref="MapletownNl3File"/>.</summary>
public static class MapletownNl3Writer {

  /// <summary>The largest value one character can carry.</summary>
  private const int _MAX_VALUE = 160;

  /// <summary>The longest run a length can express, two being the shortest worth writing.</summary>
  private const int _MAX_RUN = _MAX_VALUE + 2;

  /// <summary>Levels each channel of a palette entry can take.</summary>
  private const int _LEVELS = 9;

  /// <summary>
  /// Writes the palette and then the picture, column by column, as printable characters.
  /// </summary>
  /// <remarks>
  /// Column order is not a choice: it is how the terminal drew the picture, and the runs only
  /// compress anything because a drawing's flat areas are usually taller than they are wide.
  /// </remarks>
  public static byte[] ToBytes(MapletownNl3File file) {
    var pixels = file.Pixels ?? [];
    var palette = file.Palette ?? [];
    var body = new List<byte>();

    void Value(int value) {
      switch (value) {
        case < 95: body.Add((byte)(value + 32)); break;

        // Two three-byte sequences carry the range above ASCII, and which of them a character
        // takes is fixed by its code rather than free.
        case < 127: body.AddRange([0xEF, 0xBD, (byte)(value + 65)]); break;
        case < 159: body.AddRange([0xEF, 0xBE, (byte)(value + 1)]); break;
        case 159: body.Add(253); break;
        default: body.Add(254); break;
      }
    }

    for (var i = 0; i < MapletownNl3File.ColorCount; ++i) {
      var entry = i * 3;
      // Rounded rather than truncated, for the same reason the reduction is.
      var red = entry < palette.Length ? (palette[entry] * (_LEVELS - 1) + 127) / 255 : 0;
      var green = entry + 1 < palette.Length ? (palette[entry + 1] * (_LEVELS - 1) + 127) / 255 : 0;
      var blue = entry + 2 < palette.Length ? (palette[entry + 2] * (_LEVELS - 1) + 127) / 255 : 0;
      var color = (red * _LEVELS + green) * _LEVELS + blue;

      // A colour needs more than one character can carry, so it is split at seven bits.
      Value(color & 127);
      Value(color >> 7);
    }

    for (var x = 0; x < MapletownNl3File.Width; ++x) {
      for (var y = 0; y < MapletownNl3File.Height;) {
        var at = y * MapletownNl3File.Width + x;
        var index = at < pixels.Length ? pixels[at] & 63 : 0;

        var run = 1;
        while (run < _MAX_RUN && y + run < MapletownNl3File.Height) {
          var next = (y + run) * MapletownNl3File.Width + x;
          if (next >= pixels.Length || (pixels[next] & 63) != index)
            break;

          ++run;
        }

        // A run of one is written as the colour alone; anything longer costs a second character
        // and so pays from two upwards.
        if (run == 1)
          Value(index);
        else {
          Value(64 | index);
          Value(run - 2);
        }

        y += run;
      }
    }

    body.Add((byte)'\n');

    return body.ToArray();
  }
}
