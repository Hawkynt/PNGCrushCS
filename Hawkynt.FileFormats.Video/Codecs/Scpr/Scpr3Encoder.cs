using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Scpr;

/// <summary>
/// Writes ScreenPressor's third generation: a key frame of runs, then inter frames whose changed 16x16
/// blocks are moved from the last picture, with the last vector or a new one, or recoded as runs — the
/// whole block, or only the rectangle that changed.
/// </summary>
/// <remarks>
/// Every symbol goes through the same <see cref="Scpr3Models"/> the decoder keeps, located by the start
/// of its interval in the model's code space, so the models learn on both sides alike; the symbols are
/// then rANS-coded backwards by <see cref="Scpr3SymbolWriter"/>.
/// </remarks>
internal sealed class Scpr3Encoder(int width, int height) {

  private readonly Scpr3Models _models = new();
  private uint[]? _last;
  private readonly int _blocksWide = (width + 15) / 16;
  private readonly int _blocksHigh = (height + 15) / 16;

  /// <summary>One frame from pixels <c>(third &lt;&lt; 16) | (second &lt;&lt; 8) | first</c>, in coded row order.</summary>
  internal byte[] Encode(uint[] pixels) {
    var output = new List<byte>();
    var writer = new Scpr3SymbolWriter();
    if (this._last == null) {
      output.Add(34);
      this._Intra(writer, pixels);
    } else {
      output.Add(1);
      this._Inter(writer, pixels, this._last);
    }

    writer.WriteTo(output);
    this._last = (uint[])pixels.Clone();
    return [.. output];
  }

  private void _Units(Scpr3SymbolWriter rc, uint colour, ref int cx, ref int cx1) {
    int first = (int)(colour & 0xFF), second = (int)((colour >> 8) & 0xFF), third = (int)((colour >> 16) & 0xFF);
    Scpr3Coding.Unit(rc, this._models.Pixel(0, cx + cx1), first);
    cx1 = (cx << 6) & 0xFC0;
    cx = first >> 2;
    Scpr3Coding.Unit(rc, this._models.Pixel(1, cx + cx1), second);
    cx1 = (cx << 6) & 0xFC0;
    cx = second >> 2;
    Scpr3Coding.Unit(rc, this._models.Pixel(2, cx + cx1), third);
    cx1 = (cx << 6) & 0xFC0;
    cx = third >> 2;
  }

  private void _Intra(Scpr3SymbolWriter rc, uint[] pixels) {
    var models = this._models;
    models.Reinitialise();
    int cx = 0, cx1 = 0, k = 0, at = 0;
    while (k < width + 1) {
      var colour = pixels[at];
      this._Units(rc, colour, ref cx, ref cx1);
      var run = 1;
      while (at + run < pixels.Length && pixels[at + run] == colour && run < 255)
        ++run;
      Scpr3Coding.Value(rc, models.Run[0], run);
      k += run;
      at += run;
    }

    var ptype = 0;
    while (at < pixels.Length) {
      var (next, run) = ScprEncoder.BestIntra(pixels, width, at);
      Scpr3Coding.Value(rc, models.Op[ptype], next);
      ptype = next;
      if (ptype == 0)
        this._Units(rc, pixels[at], ref cx, ref cx1);
      Scpr3Coding.Value(rc, models.Run[ptype], run);
      at += run;
      (cx, cx1) = ScprEncoder.Context(pixels[at - 1]);
    }
  }

  /// <summary>A changed block: the rectangle that changed, and the vector it moved by if it did.</summary>
  private readonly record struct Change(int Sx1, int Sy1, int Sx2, int Sy2, int Mvx, int Mvy, bool Moved);

  private void _Inter(Scpr3SymbolWriter rc, uint[] pixels, uint[] prev) {
    var models = this._models;
    var count = this._blocksWide * this._blocksHigh;
    var changes = new Change?[count];
    int first = -1, last = -1, mvx = 0, mvy = 0;
    for (var b = 0; b < count; ++b) {
      var change = this._Classify(pixels, prev, b, ref mvx, ref mvy);
      if (change == null)
        continue;
      changes[b] = change;
      if (first < 0)
        first = b;
      last = b;
    }

    if (first < 0)
      first = last = 0; // a frame that changes nothing still names one block, left alone

    Scpr3Coding.Value(rc, models.Range, first & 0xFF);
    Scpr3Coding.Value(rc, models.Range, first >> 8);
    Scpr3Coding.Value(rc, models.Range, last & 0xFF);
    Scpr3Coding.Value(rc, models.Range, last >> 8);

    static int FillOf(Change? change) {
      if (change is not { } c)
        return 0;
      var whole = c is { Sx1: 0, Sy1: 0, Sx2: 16, Sy2: 16 };
      return c.Moved ? (whole ? 3 : 4) : (whole ? 1 : 2);
    }

    for (var b = first; b <= last;) {
      var fill = FillOf(changes[b]);
      var n = 1;
      while (b + n <= last && FillOf(changes[b + n]) == fill && n < 255)
        ++n;
      Scpr3Coding.Value(rc, models.Fill, fill);
      Scpr3Coding.Value(rc, models.Count, n);
      b += n;
    }

    var dst = (uint[])prev.Clone();
    int cx = 0, cx1 = 0;
    mvx = mvy = 0;
    for (var b = first; b <= last; ++b) {
      if (changes[b] is not { } change)
        continue;
      var fill = FillOf(change);
      int bx0 = b % this._blocksWide, by0 = b / this._blocksWide;
      if ((fill & 1) == 0) {
        Scpr3Coding.Value(rc, models.Sxy[0], change.Sx1);
        Scpr3Coding.Value(rc, models.Sxy[1], change.Sy1);
        Scpr3Coding.Value(rc, models.Sxy[2], change.Sx2 - 1);
        Scpr3Coding.Value(rc, models.Sxy[3], change.Sy2 - 1);
      }

      if (change.Moved) {
        var keep = change.Mvx == mvx && change.Mvy == mvy;
        rc.Binary(keep);
        if (!keep) {
          Scpr3Coding.Value(rc, models.Mv[0], change.Mvx + 256);
          Scpr3Coding.Value(rc, models.Mv[1], change.Mvy + 256);
          (mvx, mvy) = (change.Mvx, change.Mvy);
        }

        int top = by0 * 16, left = bx0 * 16;
        for (var i = 0; i < change.Sy2 - change.Sy1 && top + change.Sy1 + i < height && top + mvy + change.Sy1 + i < height; ++i)
          for (var j = 0; j < change.Sx2 - change.Sx1 && left + change.Sx1 + j < width && left + mvx + change.Sx1 + j < width; ++j)
            dst[(top + i + change.Sy1) * width + left + change.Sx1 + j] = prev[(top + mvy + change.Sy1 + i) * width + left + change.Sx1 + mvx + j];
        continue;
      }

      int bx = bx0 * 16 + change.Sx1, by = by0 * 16 + change.Sy1;
      var ptype = 0;
      while (by < by0 * 16 + change.Sy2 && by < height) {
        var (next, run) = this._BestRun(pixels, dst, prev, bx0, by0, change, bx, by);
        Scpr3Coding.Value(rc, models.Op[ptype], next);
        ptype = next;
        if (ptype == 0)
          this._Units(rc, pixels[by * width + bx], ref cx, ref cx1);
        Scpr3Coding.Value(rc, models.Run[ptype], run);
        var colour = ScprRuns.Inter(dst, prev, width, height, ptype, run, bx0, ref bx, ref by, pixels[by * width + bx], change.Sx1, change.Sx2);
        (cx, cx1) = ScprEncoder.Context(colour);
      }
    }
  }

  /// <summary>
  /// Whether block <paramref name="b"/> changed, the rectangle that did, and a vector that moves the
  /// last picture onto it: the last vector used if it fits, else the first scroll of up to sixteen in
  /// one direction that does.
  /// </summary>
  private Change? _Classify(uint[] pixels, uint[] prev, int b, ref int mvx, ref int mvy) {
    int left = b % this._blocksWide * 16, top = b / this._blocksWide * 16;
    int right = Math.Min(left + 16, width), bottom = Math.Min(top + 16, height);
    int x1 = int.MaxValue, y1 = int.MaxValue, x2 = -1, y2 = -1;
    for (var y = top; y < bottom; ++y)
      for (var x = left; x < right; ++x)
        if (pixels[y * width + x] != prev[y * width + x]) {
          x1 = Math.Min(x1, x);
          x2 = Math.Max(x2, x);
          y1 = Math.Min(y1, y);
          y2 = Math.Max(y2, y);
        }
    if (x2 < 0)
      return null;

    // A rectangle that reaches the block's clipped edge is stated as reaching the block's own edge.
    int sx1 = x1 - left, sy1 = y1 - top, sx2 = x2 == right - 1 ? 16 : x2 - left + 1, sy2 = y2 == bottom - 1 ? 16 : y2 - top + 1;
    if (sx1 == 0 && sy1 == 0 && sx2 == 16 && sy2 == 16 || (sx2 - sx1) * (sy2 - sy1) > 160)
      (sx1, sy1, sx2, sy2) = (0, 0, 16, 16);

    bool Fits(int dx, int dy) {
      if (dx == 0 && dy == 0 || dx is < -256 or > 255 || dy is < -256 or > 255)
        return false;
      if (top + dy + sy1 < 0 || left + dx + sx1 < 0 || top + dy + sy1 >= height || left + dx + sx1 >= width)
        return false;
      for (var y = top + sy1; y < Math.Min(top + sy2, height); ++y)
        for (var x = left + sx1; x < Math.Min(left + sx2, width); ++x) {
          int sy = y + dy, sx = x + dx;
          if (sy >= height || sx >= width || pixels[y * width + x] != prev[sy * width + sx])
            return false;
        }
      return true;
    }

    if (Fits(mvx, mvy))
      return new(sx1, sy1, sx2, sy2, mvx, mvy, true);
    for (var d = 1; d <= 16; ++d)
      foreach (var (dx, dy) in (ReadOnlySpan<(int, int)>)[(0, d), (0, -d), (d, 0), (-d, 0)])
        if (Fits(dx, dy)) {
          (mvx, mvy) = (dx, dy);
          return new(sx1, sy1, sx2, sy2, dx, dy, true);
        }

    return new(sx1, sy1, sx2, sy2, 0, 0, false);
  }

  /// <summary>Inside a block's rectangle, the longest run a copying operation reproduces from here, or a run of one colour.</summary>
  private (int Ptype, int Run) _BestRun(uint[] pixels, uint[] dst, uint[] prev, int blockX, int blockY, Change change, int bx, int by) {
    int left = blockX * 16 + change.Sx1, right = Math.Min(blockX * 16 + change.Sx2, width);
    int top = blockY * 16 + change.Sy1, bottom = Math.Min(blockY * 16 + change.Sy2, height);
    var start = by * width + bx;

    // Pixels of the rectangle already passed, the run so far included, hold their new values.
    bool Passed(int p, int current) {
      int y = p / width, x = p % width;
      if (x < left || x >= right || y < top || y >= bottom)
        return false;
      int cy = current / width, cx = current % width, sy = start / width, sx = start % width;
      return (y < cy || y == cy && x < cx) && (y > sy || y == sy && x >= sx);
    }

    int bestType = 0, bestRun = 0;
    for (var ptype = 1; ptype <= 5; ++ptype) {
      int x = bx, y = by, run = 0;
      while (run < 255 && y < bottom) {
        if (ptype is 2 or 4 or 5 && y < 1 || ptype is 1 or 4 or 5 && x == 0 && y < (ptype == 1 ? 1 : 2))
          break;
        var at = y * width + x;
        uint Here(int p) => Passed(p, at) ? pixels[p] : dst[p];
        var predicted = ptype switch {
          1 => Here(at - 1),
          2 => Here(at - width),
          3 => prev[at],
          4 => ScprRuns.Gradient(Here(at - width), Here(at - 1), Here(at - width - 1)),
          _ => Here(at - width - 1),
        };
        if (predicted != pixels[at])
          break;
        ++run;
        if (++x >= right) {
          x = left;
          ++y;
        }
      }

      if (run > bestRun)
        (bestType, bestRun) = (ptype, run);
    }

    if (bestRun > 0)
      return (bestType, bestRun);

    {
      int x = bx, y = by, run = 0;
      var colour = pixels[by * width + bx];
      while (run < 255 && y < bottom && pixels[y * width + x] == colour) {
        ++run;
        if (++x >= right) {
          x = left;
          ++y;
        }
      }

      return (0, run);
    }
  }
}
