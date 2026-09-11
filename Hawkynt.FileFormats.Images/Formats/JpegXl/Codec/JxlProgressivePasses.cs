using System;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>JPEG XL progressive-pass helpers shared by VarDCT and modular side streams.</summary>
internal static class JxlProgressivePasses {

  /// <summary>
  /// Return the channel-shift interval assigned to one progressive pass.
  /// Mirrors libjxl <c>Passes::GetDownsamplingBracket</c>: a modular channel
  /// whose <c>min(hshift, vshift)</c> lies in this inclusive interval belongs
  /// to this pass and no other one.
  /// </summary>
  public static (int MinShift, int MaxShift) GetDownsamplingBracket(
    int pass,
    int numPasses,
    ReadOnlySpan<uint> downsample,
    ReadOnlySpan<uint> lastPass
  ) {
    if (numPasses < 1)
      throw new ArgumentOutOfRangeException(nameof(numPasses), "Must be positive.");
    if ((uint)pass >= (uint)numPasses)
      throw new ArgumentOutOfRangeException(nameof(pass));
    if (downsample.Length != lastPass.Length)
      throw new ArgumentException("Progressive downsample and last-pass arrays must have equal length.");

    var maxShift = 2;
    var minShift = 3;
    for (var current = 0; ; ++current) {
      for (var i = 0; i < downsample.Length; ++i) {
        if (lastPass[i] != (uint)current)
          continue;

        minShift = downsample[i] switch {
          8 => 3,
          4 => 2,
          2 => 1,
          1 => 0,
          _ => throw new InvalidDataException(
            $"JPEG XL progressive downsampling factor {downsample[i]} is not one of 1, 2, 4 or 8."),
        };
      }

      // The final pass always owns full-resolution (shift-zero) channels.
      if (current == numPasses - 1)
        minShift = 0;
      if (current == pass)
        return (minShift, maxShift);

      maxShift = minShift - 1;
    }
  }
}
