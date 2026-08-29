using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>High-profile Intra_8x8 reference filtering and the nine prediction modes of H.264 clause 8.3.2.</summary>
internal static class H264Intra8x8Prediction {

  internal static void Predict(
    int mode,
    ReadOnlySpan<byte> top,
    ReadOnlySpan<byte> left,
    byte topLeft,
    bool topAvailable,
    bool topRightAvailable,
    bool leftAvailable,
    bool topLeftAvailable,
    Span<byte> output) {
    if ((uint)mode > 8)
      throw new InvalidDataException($"H.264 Intra_8x8 prediction mode {mode} is outside 0..8.");
    if (top.Length < 16)
      throw new ArgumentException("Intra_8x8 prediction needs sixteen top/top-right samples.", nameof(top));
    if (left.Length < 8)
      throw new ArgumentException("Intra_8x8 prediction needs eight left samples.", nameof(left));
    if (output.Length < 64)
      throw new ArgumentException("Intra_8x8 prediction needs a 64-sample output buffer.", nameof(output));

    var needsTop = mode is 0 or 3 or 4 or 5 or 6 or 7;
    var needsLeft = mode is 1 or 4 or 5 or 6 or 8;
    var needsCorner = mode is 4 or 5 or 6;
    if (needsTop && !topAvailable)
      throw new InvalidDataException($"H.264 Intra_8x8 mode {mode} requires top reference samples that are unavailable.");
    if (needsLeft && !leftAvailable)
      throw new InvalidDataException($"H.264 Intra_8x8 mode {mode} requires left reference samples that are unavailable.");
    if (needsCorner && !topLeftAvailable)
      throw new InvalidDataException($"H.264 Intra_8x8 mode {mode} requires the top-left reference sample.");

    Span<byte> rawTop = stackalloc byte[16];
    Span<byte> filteredTop = stackalloc byte[16];
    Span<byte> filteredLeft = stackalloc byte[8];

    if (topAvailable) {
      top[..8].CopyTo(rawTop);
      if (topRightAvailable)
        top.Slice(8, 8).CopyTo(rawTop[8..]);
      else
        rawTop[8..].Fill(rawTop[7]);

      filteredTop[0] = (byte)(topLeftAvailable
        ? (topLeft + 2 * rawTop[0] + rawTop[1] + 2) >> 2
        : (3 * rawTop[0] + rawTop[1] + 2) >> 2);
      for (var x = 1; x < 15; ++x)
        filteredTop[x] = (byte)((rawTop[x - 1] + 2 * rawTop[x] + rawTop[x + 1] + 2) >> 2);
      filteredTop[15] = (byte)((rawTop[14] + 3 * rawTop[15] + 2) >> 2);
    }

    if (leftAvailable) {
      filteredLeft[0] = (byte)(topLeftAvailable
        ? (topLeft + 2 * left[0] + left[1] + 2) >> 2
        : (3 * left[0] + left[1] + 2) >> 2);
      for (var y = 1; y < 7; ++y)
        filteredLeft[y] = (byte)((left[y - 1] + 2 * left[y] + left[y + 1] + 2) >> 2);
      filteredLeft[7] = (byte)((left[6] + 3 * left[7] + 2) >> 2);
    }

    var filteredTopLeft = topLeftAvailable
      ? topAvailable && leftAvailable
        ? (byte)((rawTop[0] + 2 * topLeft + left[0] + 2) >> 2)
        : topAvailable
          ? (byte)((3 * topLeft + rawTop[0] + 2) >> 2)
          : leftAvailable
            ? (byte)((3 * topLeft + left[0] + 2) >> 2)
            : topLeft
      : (byte)0;

    switch (mode) {
      case 0: _Vertical(filteredTop, output); break;
      case 1: _Horizontal(filteredLeft, output); break;
      case 2: _Dc(filteredTop, filteredLeft, topAvailable, leftAvailable, output); break;
      case 3: _DiagonalDownLeft(filteredTop, output); break;
      case 4: _DiagonalDownRight(filteredTop, filteredLeft, filteredTopLeft, output); break;
      case 5: _VerticalRight(filteredTop, filteredLeft, filteredTopLeft, output); break;
      case 6: _HorizontalDown(filteredTop, filteredLeft, filteredTopLeft, output); break;
      case 7: _VerticalLeft(filteredTop, output); break;
      case 8: _HorizontalUp(filteredLeft, output); break;
    }
  }

  private static void _Vertical(ReadOnlySpan<byte> top, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      top[..8].CopyTo(output.Slice(y * 8, 8));
  }

  private static void _Horizontal(ReadOnlySpan<byte> left, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      output.Slice(y * 8, 8).Fill(left[y]);
  }

  private static void _Dc(
    ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, bool topAvailable, bool leftAvailable, Span<byte> output) {
    var value = 128;
    if (topAvailable && leftAvailable) {
      var sum = 0;
      for (var i = 0; i < 8; ++i)
        sum += top[i] + left[i];
      value = (sum + 8) >> 4;
    } else if (topAvailable) {
      var sum = 0;
      for (var i = 0; i < 8; ++i)
        sum += top[i];
      value = (sum + 4) >> 3;
    } else if (leftAvailable) {
      var sum = 0;
      for (var i = 0; i < 8; ++i)
        sum += left[i];
      value = (sum + 4) >> 3;
    }
    output[..64].Fill((byte)value);
  }

  private static void _DiagonalDownLeft(ReadOnlySpan<byte> top, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var sum = x + y;
        output[y * 8 + x] = sum == 14
          ? (byte)((top[14] + 3 * top[15] + 2) >> 2)
          : (byte)((top[sum] + 2 * top[sum + 1] + top[sum + 2] + 2) >> 2);
      }
  }

  private static void _DiagonalDownRight(
    ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte topLeft, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var value = x > y
          ? (_Top(top, topLeft, x - y - 2) + 2 * _Top(top, topLeft, x - y - 1) + _Top(top, topLeft, x - y) + 2) >> 2
          : x < y
            ? (_Left(left, topLeft, y - x - 2) + 2 * _Left(left, topLeft, y - x - 1) + _Left(left, topLeft, y - x) + 2) >> 2
            : (top[0] + 2 * topLeft + left[0] + 2) >> 2;
        output[y * 8 + x] = (byte)value;
      }
  }

  private static void _VerticalRight(
    ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte topLeft, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var z = 2 * x - y;
        int value;
        if ((z & 1) == 0 && z >= 0) {
          var xp = x - (y >> 1) - 1;
          value = (_Top(top, topLeft, xp) + _Top(top, topLeft, xp + 1) + 1) >> 1;
        } else if (z > 0) {
          var xp = x - (y >> 1) - 2;
          value = (_Top(top, topLeft, xp) + 2 * _Top(top, topLeft, xp + 1) + _Top(top, topLeft, xp + 2) + 2) >> 2;
        } else if (z == -1) {
          value = (left[0] + 2 * topLeft + top[0] + 2) >> 2;
        } else {
          value = (_Left(left, topLeft, y - 2 * x - 1)
                   + 2 * _Left(left, topLeft, y - 2 * x - 2)
                   + _Left(left, topLeft, y - 2 * x - 3) + 2) >> 2;
        }
        output[y * 8 + x] = (byte)value;
      }
  }

  private static void _HorizontalDown(
    ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte topLeft, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var z = 2 * y - x;
        int value;
        if ((z & 1) == 0 && z >= 0) {
          var yp = y - (x >> 1) - 1;
          value = (_Left(left, topLeft, yp) + _Left(left, topLeft, yp + 1) + 1) >> 1;
        } else if (z > 0) {
          var yp = y - (x >> 1) - 2;
          value = (_Left(left, topLeft, yp) + 2 * _Left(left, topLeft, yp + 1) + _Left(left, topLeft, yp + 2) + 2) >> 2;
        } else if (z == -1) {
          value = (left[0] + 2 * topLeft + top[0] + 2) >> 2;
        } else {
          value = (_Top(top, topLeft, x - 2 * y - 1)
                   + 2 * _Top(top, topLeft, x - 2 * y - 2)
                   + _Top(top, topLeft, x - 2 * y - 3) + 2) >> 2;
        }
        output[y * 8 + x] = (byte)value;
      }
  }

  private static void _VerticalLeft(ReadOnlySpan<byte> top, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var xp = x + (y >> 1);
        output[y * 8 + x] = (y & 1) == 0
          ? (byte)((top[xp] + top[xp + 1] + 1) >> 1)
          : (byte)((top[xp] + 2 * top[xp + 1] + top[xp + 2] + 2) >> 2);
      }
  }

  private static void _HorizontalUp(ReadOnlySpan<byte> left, Span<byte> output) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var z = x + 2 * y;
        int value;
        if ((z & 1) == 0 && z <= 12) {
          var yp = y + (x >> 1);
          value = (left[yp] + left[yp + 1] + 1) >> 1;
        } else if ((z & 1) != 0 && z <= 11) {
          var yp = y + (x >> 1);
          var p1 = left[Math.Min(yp + 1, 7)];
          var p2 = left[Math.Min(yp + 2, 7)];
          value = (left[yp] + 2 * p1 + p2 + 2) >> 2;
        } else if (z == 13) {
          value = (left[6] + 3 * left[7] + 2) >> 2;
        } else {
          value = left[7];
        }
        output[y * 8 + x] = (byte)value;
      }
  }

  private static int _Top(ReadOnlySpan<byte> top, byte topLeft, int x)
    => x == -1 ? topLeft : top[x];

  private static int _Left(ReadOnlySpan<byte> left, byte topLeft, int y)
    => y == -1 ? topLeft : left[y];
}
