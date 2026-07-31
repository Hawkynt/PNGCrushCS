using System;
using System.Collections.Generic;

namespace FileFormat.ArtMaster88;

/// <summary>Assembles Art Master 88 picture bytes from an <see cref="ArtMaster88File"/>.</summary>
public static class ArtMaster88Writer {

  /// <summary>
  /// Writes the PC-88 form, whose three planes are the three colour channels and which therefore
  /// needs no palette.
  /// </summary>
  /// <remarks>
  /// The other form would need a palette chosen for the picture, and there is no right answer to
  /// that; this one has no choice to make at all.
  /// </remarks>
  public static byte[] ToBytes(ArtMaster88File file) {
    var planes = file.Planes ?? [];
    var body = new List<byte>(new byte[40]);
    "SS_SIF    0.0"u8.CopyTo(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body));

    body[19] = (byte)'B';
    body[20] = (byte)'R';
    body[21] = (byte)'G';
    body[24] = 128;
    body[25] = 2;
    body[26] = 200;

    for (var plane = 0; plane < 3 && plane < planes.Length; ++plane)
      _Pack(body, planes[plane]);

    return body.ToArray();
  }

  /// <summary>
  /// Packs one plane. A run is marked by repeating a byte, so the marker costs nothing until it is
  /// needed — and a byte that happens to equal the one before it cannot be written plainly at all,
  /// since the reader would take it for a marker.
  /// </summary>
  private static void _Pack(List<byte> body, ReadOnlySpan<byte> plane) {
    var escape = -1;

    for (var i = 0; i < plane.Length;) {
      var value = plane[i];
      var run = 1;
      while (i + run < plane.Length && plane[i + run] == value)
        ++run;

      // A value the reader would already take as a marker has to be written as a run, however
      // short: there is no other way to say it.
      if (value == escape) {
        var repeats = Math.Min(run, 255);
        body.Add(value);

        // The count includes the byte that marked it, and wraps, so 255 repeats is written as nought.
        body.Add((byte)((repeats + 1) & 255));
        escape = -1;
        i += repeats;
        continue;
      }

      body.Add(value);
      escape = value;
      ++i;

      // A run of two costs the same either way, so the marker only pays from three upwards.
      var rest = run - 1;
      if (rest < 2)
        continue;

      var more = Math.Min(rest, 255);
      body.Add(value);
      body.Add((byte)((more + 1) & 255));
      escape = -1;
      i += more;
    }
  }
}
