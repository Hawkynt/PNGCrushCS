using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Multi-resolution squeeze transform for JPEG XL modular mode.
/// Splits a channel into average and difference sub-channels at half resolution.
/// The inverse reconstructs full resolution from these sub-channels.
/// Squeeze is applied recursively to produce a multi-scale decomposition.
/// </summary>
internal static class JxlSqueezeTransform {

  /// <summary>
  /// Apply inverse horizontal squeeze: reconstruct full-width channel from
  /// average (left half) and difference (right half) sub-channels.
  /// For each row: avg[x], diff[x] -> even[x] = avg + (diff+1)/2, odd[x] = even[x] - diff
  /// </summary>
  /// <param name="channel">Channel data with averages in left half, differences in right half.</param>
  /// <param name="width">Full target width.</param>
  /// <param name="height">Channel height.</param>
  public static void InverseHorizontal(int[,] channel, int width, int height) {
    ArgumentNullException.ThrowIfNull(channel);

    var halfW = (width + 1) / 2;
    var temp = new int[width];

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < halfW && x < channel.GetLength(1); ++x) {
        var avg = channel[y, x];
        var diff = x + halfW < channel.GetLength(1) ? channel[y, x + halfW] : 0;

        var even = avg + (diff + 1) / 2;
        var odd = even - diff;

        var ex = x * 2;
        if (ex < width)
          temp[ex] = even;
        if (ex + 1 < width)
          temp[ex + 1] = odd;
      }

      for (var x = 0; x < width; ++x)
        channel[y, x] = temp[x];
    }
  }

  /// <summary>
  /// Apply inverse vertical squeeze: reconstruct full-height channel from
  /// average (top half) and difference (bottom half) sub-channels.
  /// For each column: avg[y], diff[y] -> even[y] = avg + (diff+1)/2, odd[y] = even[y] - diff
  /// </summary>
  /// <param name="channel">Channel data with averages in top half, differences in bottom half.</param>
  /// <param name="width">Channel width.</param>
  /// <param name="height">Full target height.</param>
  public static void InverseVertical(int[,] channel, int width, int height) {
    ArgumentNullException.ThrowIfNull(channel);

    var halfH = (height + 1) / 2;
    var temp = new int[height];

    for (var x = 0; x < width; ++x) {
      for (var y = 0; y < halfH && y < channel.GetLength(0); ++y) {
        var avg = channel[y, x];
        var diff = y + halfH < channel.GetLength(0) ? channel[y + halfH, x] : 0;

        var even = avg + (diff + 1) / 2;
        var odd = even - diff;

        var ey = y * 2;
        if (ey < height)
          temp[ey] = even;
        if (ey + 1 < height)
          temp[ey + 1] = odd;
      }

      for (var y = 0; y < height; ++y)
        channel[y, x] = temp[y];
    }
  }

  /// <summary>
  /// Apply forward horizontal squeeze: split full-width channel into
  /// average (left half) and difference (right half) sub-channels.
  /// </summary>
  /// <param name="channel">Channel data at full resolution.</param>
  /// <param name="width">Current width (will produce halfW averages + halfW differences).</param>
  /// <param name="height">Channel height.</param>
  public static void ForwardHorizontal(int[,] channel, int width, int height) {
    ArgumentNullException.ThrowIfNull(channel);

    var halfW = (width + 1) / 2;
    var temp = new int[width];

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < halfW; ++x) {
        var ex = x * 2;
        var even = channel[y, ex];
        var odd = ex + 1 < width ? channel[y, ex + 1] : even;

        var diff = even - odd;
        var avg = odd + (diff + 1) / 2; // = (even + odd + 1) / 2 when diff is positive

        temp[x] = avg;
        if (x + halfW < width)
          temp[x + halfW] = diff;
      }

      for (var x = 0; x < width; ++x)
        channel[y, x] = temp[x];
    }
  }

  /// <summary>
  /// Apply forward vertical squeeze: split full-height channel into
  /// average (top half) and difference (bottom half) sub-channels.
  /// </summary>
  /// <param name="channel">Channel data at full resolution.</param>
  /// <param name="width">Channel width.</param>
  /// <param name="height">Current height (will produce halfH averages + halfH differences).</param>
  public static void ForwardVertical(int[,] channel, int width, int height) {
    ArgumentNullException.ThrowIfNull(channel);

    var halfH = (height + 1) / 2;
    var temp = new int[height];

    for (var x = 0; x < width; ++x) {
      for (var y = 0; y < halfH; ++y) {
        var ey = y * 2;
        var even = channel[ey, x];
        var odd = ey + 1 < height ? channel[ey + 1, x] : even;

        var diff = even - odd;
        var avg = odd + (diff + 1) / 2;

        temp[y] = avg;
        if (y + halfH < height)
          temp[y + halfH] = diff;
      }

      for (var y = 0; y < height; ++y)
        channel[y, x] = temp[y];
    }
  }

  /// <summary>
  /// Apply a full squeeze decomposition (horizontal + vertical) at one level.
  /// </summary>
  public static void ForwardSqueeze(int[,] channel, int width, int height) {
    ForwardHorizontal(channel, width, height);
    ForwardVertical(channel, width, height);
  }

  /// <summary>
  /// Apply a full inverse squeeze (vertical then horizontal) at one level.
  /// </summary>
  public static void InverseSqueeze(int[,] channel, int width, int height) {
    InverseVertical(channel, width, height);
    InverseHorizontal(channel, width, height);
  }
}
