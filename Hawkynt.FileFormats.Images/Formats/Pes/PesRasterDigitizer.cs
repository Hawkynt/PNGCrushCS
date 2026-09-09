using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Pes;

/// <summary>Turns a raster into a simple, deterministic PES stitch plan.</summary>
internal static class PesRasterDigitizer {

  // The Brother/PEC chart has 65 named colours. PesThreadChart is 256 entries because the on-disk
  // index is one byte; entries after the named chart are compatibility padding and all black.
  private const int _NamedThreadCount = 65;

  public static PesFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Width <= 0 || image.Height <= 0 || !image.HasEnoughPixelData)
      throw new ArgumentException("PES requires a non-empty image with a complete pixel buffer.", nameof(image));

    // PEC stores design extents as unsigned 16-bit distances. Coordinates themselves are deltas and
    // may be much larger, but a canvas wider than 65536 samples cannot be stated honestly.
    if (image.Width > ushort.MaxValue + 1 || image.Height > ushort.MaxValue + 1)
      throw new ArgumentOutOfRangeException(nameof(image), "PES dimensions may not exceed 65536x65536.");

    var rgba = image.ToRgba32();
    var rowThreads = new int[image.Width];
    var builders = new _BlockBuilder?[_NamedThreadCount];
    var order = new List<int>();

    for (var y = 0; y < image.Height; ++y) {
      var rowOffset = checked(y * image.Width * 4);
      for (var x = 0; x < image.Width; ++x) {
        var at = rowOffset + x * 4;
        var alpha = rgba[at + 3];
        if (alpha == 0) {
          rowThreads[x] = -1;
          continue;
        }

        var r = rgba[at];
        var g = rgba[at + 1];
        var b = rgba[at + 2];

        // PES has no alpha. Its renderer uses white cloth, so partially transparent pixels are
        // composited onto white before choosing a thread rather than made artificially darker.
        if (alpha != byte.MaxValue) {
          var inverse = byte.MaxValue - alpha;
          r = (byte)((r * alpha + byte.MaxValue * inverse + 127) / byte.MaxValue);
          g = (byte)((g * alpha + byte.MaxValue * inverse + 127) / byte.MaxValue);
          b = (byte)((b * alpha + byte.MaxValue * inverse + 127) / byte.MaxValue);
        }

        rowThreads[x] = _NearestThread(r, g, b);
      }

      for (var x = 0; x < image.Width;) {
        var thread = rowThreads[x];
        if (thread < 0) {
          ++x;
          continue;
        }

        var start = x++;
        while (x < image.Width && rowThreads[x] == thread)
          ++x;
        var end = x - 1;

        var builder = builders[thread];
        if (builder is null) {
          builder = builders[thread] = new(thread);
          order.Add(thread);
        }

        // Reposition without sewing, then sew the run. Duplicating a one-pixel run deliberately
        // produces a zero-length stitch: the renderer and an embroidery machine both mark it.
        builder.JumpIndices.Add(builder.Points.Count);
        builder.Points.Add((start, y));
        builder.Points.Add((end, y));
      }
    }

    if (order.Count == 0)
      throw new ArgumentException("PES cannot represent a fully transparent image because it contains no stitches.", nameof(image));

    var blocks = new PesStitchBlock[order.Count];
    for (var blockIndex = 0; blockIndex < order.Count; ++blockIndex) {
      var builder = builders[order[blockIndex]]!;
      var anchorCount = blockIndex == 0 ? 2 : 0;
      var points = new (int X, int Y)[builder.Points.Count + anchorCount];
      var jumps = new int[builder.JumpIndices.Count + anchorCount];

      if (anchorCount != 0) {
        // Invisible jumps preserve the source canvas when the first/last visible pixels are
        // transparent. A PES does not otherwise carry a raster canvas size.
        points[0] = (0, 0);
        points[1] = (image.Width - 1, image.Height - 1);
        jumps[0] = 0;
        jumps[1] = 1;
      }

      builder.Points.CopyTo(points, anchorCount);
      for (var i = 0; i < builder.JumpIndices.Count; ++i)
        jumps[i + anchorCount] = builder.JumpIndices[i] + anchorCount;

      blocks[blockIndex] = new PesStitchBlock {
        ThreadIndex = builder.ThreadIndex,
        Color = PesThreadChart.Colors[builder.ThreadIndex],
        Points = points,
        JumpIndices = jumps,
      };
    }

    return new PesFile {
      Version = "0001",
      Blocks = blocks,
      MinX = 0,
      MinY = 0,
      MaxX = image.Width - 1,
      MaxY = image.Height - 1,
    };
  }

  private static int _NearestThread(byte r, byte g, byte b) {
    var bestIndex = 0;
    var bestDistance = int.MaxValue;

    for (var i = 0; i < _NamedThreadCount; ++i) {
      var color = PesThreadChart.Colors[i];
      var dr = r - (byte)(color >> 16);
      var dg = g - (byte)(color >> 8);
      var db = b - (byte)color;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      bestIndex = i;
      if (distance == 0)
        break;
    }

    return bestIndex;
  }

  private sealed class _BlockBuilder(int threadIndex) {
    public int ThreadIndex { get; } = threadIndex;
    public List<(int X, int Y)> Points { get; } = [];
    public List<int> JumpIndices { get; } = [];
  }
}
