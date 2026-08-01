using System;
using System.Collections.Generic;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>The run-length coding a Spooky Sprites picture uses.</summary>
/// <remarks>
/// Literal runs and repeat runs strictly alternate, beginning with a literal one. A literal run
/// states how many colours follow and then gives them, two bytes each; a repeat run states only how
/// many more copies of the last colour to draw, and carries no data at all. Because they alternate,
/// a repeat run of zero cannot be expressed — which is why a picture is cut at the point a colour
/// starts repeating rather than wherever a fixed block would end.
/// <para/>
/// A count of 255 is not a count but a marker: 255 plus a big-endian word follows, so a run can
/// reach 65790.
/// <para/>
/// What was here before was a signed byte — positive for literals, negative for repeats, zero to
/// end — which is the Macintosh scheme rather than this one.
/// </remarks>
internal static class SpookySpritesFalconRleCompressor {

  /// <summary>The largest count a single byte states before the extension is needed.</summary>
  private const int _InlineMax = 254;

  /// <summary>The marker that says a longer count follows.</summary>
  private const int _Extended = 255;

  public static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedPixelCount) {
    var result = new byte[expectedPixelCount * 2];
    var at = 0;
    var written = 0;
    var literal = true;
    byte lastHigh = 0, lastLow = 0;

    while (written < expectedPixelCount && at < compressed.Length) {
      int count = compressed[at++];
      if (count == 0)
        break;

      if (count == _Extended) {
        if (at + 1 >= compressed.Length)
          break;

        count = _Extended + (compressed[at] << 8) + compressed[at + 1];
        at += 2;
      }

      for (var i = 0; i < count && written < expectedPixelCount; ++i) {
        if (literal) {
          if (at + 1 >= compressed.Length)
            return result;

          lastHigh = compressed[at];
          lastLow = compressed[at + 1];
          at += 2;
        }

        result[written * 2] = lastHigh;
        result[written * 2 + 1] = lastLow;
        ++written;
      }

      literal = !literal;
    }

    return result;
  }

  public static byte[] Compress(ReadOnlySpan<byte> pixelData, int pixelCount) {
    var result = new List<byte>(pixelCount);
    var i = 0;

    while (i < pixelCount) {
      // Literals run until a colour repeats, which is where the alternation hands over.
      var start = i;
      ++i;
      while (i < pixelCount && !_Same(pixelData, i, i - 1))
        ++i;

      _Count(result, i - start);
      for (var j = start; j < i; ++j) {
        result.Add(pixelData[j * 2]);
        result.Add(pixelData[j * 2 + 1]);
      }

      if (i >= pixelCount)
        break;

      var repeats = 0;
      while (i < pixelCount && _Same(pixelData, i, i - 1 - repeats)) {
        ++i;
        ++repeats;
      }

      _Count(result, repeats);
    }

    return result.ToArray();
  }

  private static bool _Same(ReadOnlySpan<byte> data, int a, int b)
    => data[a * 2] == data[b * 2] && data[a * 2 + 1] == data[b * 2 + 1];

  private static void _Count(List<byte> target, int count) {
    if (count <= _InlineMax) {
      target.Add((byte)count);
      return;
    }

    target.Add(_Extended);
    var extra = count - _Extended;
    target.Add((byte)(extra >> 8));
    target.Add((byte)extra);
  }
}
