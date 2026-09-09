using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The pictures an oracle is handed, and the sizes each format will actually take one at.
/// </summary>
/// <remarks>
/// Most formats here accept one size or a handful and throw at any other, so a fixture that always
/// asks for 320 by 200 measures which writers refuse a picture rather than which write a bad one.
/// The sizes a format declares are the sizes it is asked for.
/// <para/>
/// The colour count travels with the size for the same reason. A format that stores four colours and
/// one that stores sixteen million both declare it here, and handing the second one's gradient to
/// the first is asking for something the format never claimed to hold.
/// </remarks>
internal static class OracleProbePicture {

  /// <summary>The pictures this format says it takes, most likely first.</summary>
  public static IEnumerable<(int Width, int Height, VideoMode? Mode)> CasesFor(FormatEntry entry) {
    var seen = new HashSet<(int, int)>();

    foreach (var mode in entry.VideoModes ?? [])
    foreach (var (widths, heights) in mode.Dimensions)
    foreach (var width in _Candidates(widths, 320))
    foreach (var height in _Candidates(heights, 200)) {
      if ((long)width * height is <= 0 or > 4096L * 4096L || !seen.Add((width, height)))
        continue;

      yield return (width, height, mode);
    }

    if (seen.Add((320, 200)))
      yield return (320, 200, null);

    // And a small one. A few coders here write a picture of the ordinary size that nothing can read
    // and a small one that everything can — WebP's lossless writer is the case that showed it — and
    // stopping at the first size would record those as never having been looked at, which is a
    // different and much stronger statement than "it has been looked at and it is not always right".
    if (seen.Add((8, 8)))
      yield return (8, 8, null);
  }

  /// <summary>A picture the mode can hold: full colour, or indexed within its colour count.</summary>
  public static RawImage Sample(int width, int height, VideoMode? mode) {
    var colours = mode?.MaxColourCount ?? int.MaxValue;

    return colours < 256 ? _Indexed(width, height, Math.Max(2, colours)) : _FullColour(width, height);
  }

  /// <summary>What to try for one dimension: the stated values, or a default where anything goes.</summary>
  private static IEnumerable<int> _Candidates(IntegerRange range, int whenUnbounded) {
    if (range.Min == 1 && range.Max == int.MaxValue) {
      yield return whenUnbounded;
      yield break;
    }

    var preferred = range.SnapToValid(whenUnbounded);
    yield return preferred;

    if (range.Min != preferred)
      yield return range.Min;

    var max = range.SnapToValid(range.Max);
    if (max != preferred && max != range.Min && range.Max < 4096)
      yield return max;
  }

  private static RawImage _FullColour(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 4;
      data[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      data[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      data[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
      data[at + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  /// <summary>The same gradient in <paramref name="colours"/> indices, every one of them used.</summary>
  private static RawImage _Indexed(int width, int height, int colours) {
    var palette = new byte[colours * 3];
    for (var i = 0; i < colours; ++i) {
      // A ramp rather than arbitrary colours: a writer that reorders or drops entries then shows up
      // as a picture out of order, which is visible, instead of as noise that looks decoded.
      var level = (byte)(i * 255 / Math.Max(1, colours - 1));
      palette[i * 3] = level;
      palette[i * 3 + 1] = (byte)(255 - level);
      palette[i * 3 + 2] = (byte)(i % 2 == 0 ? 255 : 0);
    }

    var data = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      data[y * width + x] = (byte)((x * colours / width + y / 8) % colours);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = data,
      Palette = palette,
      PaletteCount = colours,
    };
  }
}
