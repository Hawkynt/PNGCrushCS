using System;
using System.Collections.Generic;

namespace FileFormat.FastgraphPixelRun;

/// <summary>Assembles a Fastgraph pixel run picture: the header, then the runs from the bottom row up.</summary>
public static class FastgraphPixelRunWriter {

  public static byte[] ToBytes(FastgraphPixelRunFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width is < 1 or > FastgraphPixelRunFile.MaxDimension || height is < 1 or > FastgraphPixelRunFile.MaxDimension)
      throw new ArgumentException($"A Fastgraph picture is at most {FastgraphPixelRunFile.MaxDimension} on a side and this is {width}x{height}.", nameof(file));

    var pixels = file.Pixels ?? [];
    if (pixels.Length != width * height)
      throw new ArgumentException($"A Fastgraph picture of {width}x{height} needs {width * height} bytes and has {pixels.Length}.", nameof(file));

    var result = new List<byte>(FastgraphPixelRunFile.HeaderSize + pixels.Length / 4);
    for (var i = 0; i < FastgraphPixelRunFile.Magic.Length; ++i)
      result.Add(FastgraphPixelRunFile.Magic[i]);

    _WriteWord(result, width - 1);
    _WriteWord(result, height - 1);
    result.Add(0);
    result.Add(0);

    // The runs are taken over the picture read bottom row first, and they cross rows where the colour
    // does — which is what the files do, and what makes writing one back give the bytes it came from.
    var total = pixels.Length;
    for (var at = 0; at < total;) {
      var colour = _At(pixels, width, height, at);
      var run = 1;
      while (run < FastgraphPixelRunFile.MaxRun && at + run < total && _At(pixels, width, height, at + run) == colour)
        ++run;

      result.Add(colour);
      result.Add((byte)run);
      at += run;
    }

    return result.ToArray();
  }

  /// <summary>The pixel a run stream reaches after <paramref name="step"/> pixels, counting from the bottom row.</summary>
  private static byte _At(byte[] pixels, int width, int height, int step)
    => pixels[(height - 1 - step / width) * width + step % width];

  /// <summary>Writes a two-byte header value the way these files hold them: each byte, then a zero.</summary>
  private static void _WriteWord(List<byte> target, int value) {
    target.Add((byte)value);
    target.Add(0);
    target.Add((byte)(value >> 8));
    target.Add(0);
  }
}
