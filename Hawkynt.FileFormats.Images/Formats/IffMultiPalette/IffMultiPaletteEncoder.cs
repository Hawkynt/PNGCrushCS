using System;
using FileFormat.Core;

namespace FileFormat.IffMultiPalette;

/// <summary>Fits an RGB raster to the register budget of a non-interlaced small-form PCHG picture.</summary>
internal static class IffMultiPaletteEncoder {

  private const int _ColourSpaceSize = 1 << 12;
  private const int _CandidateAttempts = 16;

  public static IffMultiPaletteFile Encode(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"IFF multi-palette width must be between 1 and {ushort.MaxValue} pixels.", nameof(image));
    if (image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"IFF multi-palette height must be between 1 and {ushort.MaxValue} pixels.", nameof(image));

    int pixelCount;
    int paletteByteCount;
    try {
      pixelCount = checked(image.Width * image.Height);
      paletteByteCount = checked(image.Height * IffMultiPaletteFile.PaletteByteSize);
    } catch (OverflowException exception) {
      throw new ArgumentException("IFF multi-palette dimensions are too large for an in-memory picture.", nameof(image), exception);
    }

    var source = image.EnsureFormat(PixelFormat.Rgb24);
    var indices = new byte[pixelCount];
    var scanlinePalettes = new byte[paletteByteCount];
    var palette = new ushort[IffMultiPaletteFile.PaletteEntries];
    var histogram = new int[_ColourSpaceSize];
    var line = new ushort[image.Width];
    var nearestIndex = new byte[_ColourSpaceSize];
    var nearestDistance = new ushort[_ColourSpaceSize];
    var secondDistance = new ushort[_ColourSpaceSize];
    var scores = new long[_ColourSpaceSize];

    for (var y = 0; y < image.Height; ++y) {
      Array.Clear(histogram);
      var sourceAt = y * image.Width * 3;
      for (var x = 0; x < image.Width; ++x) {
        var colour = _PackColour(
          source.PixelData[sourceAt + x * 3],
          source.PixelData[sourceAt + x * 3 + 1],
          source.PixelData[sourceAt + x * 3 + 2]);
        line[x] = colour;
        ++histogram[colour];
      }

      _OptimisePalette(
        histogram,
        palette,
        y == 0 ? IffMultiPaletteFile.PaletteEntries : IffMultiPaletteFile.MaxChangesPerLine,
        nearestIndex,
        nearestDistance,
        secondDistance,
        scores);

      var paletteAt = y * IffMultiPaletteFile.PaletteByteSize;
      for (var register = 0; register < palette.Length; ++register)
        _ExpandColour(palette[register], scanlinePalettes.AsSpan(paletteAt + register * 3, 3));

      var pixelAt = y * image.Width;
      for (var x = 0; x < image.Width; ++x)
        indices[pixelAt + x] = (byte)_NearestRegister(line[x], palette, out _, out _);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = indices,
      ScanlinePalettes = scanlinePalettes,
    };
  }

  /// <summary>
  /// Adds the source colours that remove the most weighted error, replacing only a register whose
  /// removal is cheaper than the improvement.  Starting with the previous line's palette makes the
  /// number of accepted replacements the number of PCHG changes that line needs.
  /// </summary>
  private static void _OptimisePalette(
    int[] histogram,
    ushort[] palette,
    int updateBudget,
    byte[] nearestIndex,
    ushort[] nearestDistance,
    ushort[] secondDistance,
    long[] scores) {

    Span<long> replacementAdjustment = stackalloc long[IffMultiPaletteFile.PaletteEntries];

    for (var update = 0; update < updateBudget; ++update) {
      Array.Clear(scores);
      for (var colour = 0; colour < histogram.Length; ++colour) {
        var count = histogram[colour];
        if (count == 0)
          continue;

        var nearest = _NearestRegister((ushort)colour, palette, out var distance, out var second);
        nearestIndex[colour] = (byte)nearest;
        nearestDistance[colour] = (ushort)distance;
        secondDistance[colour] = (ushort)second;
        scores[colour] = (long)count * distance;
      }

      var replaced = false;
      for (var attempt = 0; attempt < _CandidateAttempts; ++attempt) {
        var candidate = _HighestScore(scores);
        if (candidate < 0 || scores[candidate] == 0)
          break;
        scores[candidate] = -1;

        replacementAdjustment.Clear();
        long commonDelta = 0;
        for (var colour = 0; colour < histogram.Length; ++colour) {
          var count = histogram[colour];
          if (count == 0)
            continue;

          var oldDistance = nearestDistance[colour];
          var candidateDistance = _Distance((ushort)colour, (ushort)candidate);
          var withCandidate = Math.Min(oldDistance, candidateDistance);
          commonDelta += (long)count * (withCandidate - oldDistance);

          var nearest = nearestIndex[colour];
          replacementAdjustment[nearest] += (long)count * (Math.Min(secondDistance[colour], candidateDistance) - withCandidate);
        }

        var bestRegister = 0;
        var bestDelta = commonDelta + replacementAdjustment[0];
        for (var register = 1; register < palette.Length; ++register) {
          var delta = commonDelta + replacementAdjustment[register];
          if (delta < bestDelta) {
            bestDelta = delta;
            bestRegister = register;
          }
        }

        if (bestDelta >= 0)
          continue;

        palette[bestRegister] = (ushort)candidate;
        replaced = true;
        break;
      }

      if (!replaced)
        break;
    }
  }

  private static int _HighestScore(long[] scores) {
    var best = -1;
    long bestScore = -1;
    for (var colour = 0; colour < scores.Length; ++colour)
      if (scores[colour] > bestScore) {
        best = colour;
        bestScore = scores[colour];
      }

    return best;
  }

  private static int _NearestRegister(ushort colour, ushort[] palette, out int nearestDistance, out int secondDistance) {
    var nearest = 0;
    nearestDistance = int.MaxValue;
    secondDistance = int.MaxValue;

    for (var register = 0; register < palette.Length; ++register) {
      var distance = _Distance(colour, palette[register]);
      if (distance < nearestDistance) {
        secondDistance = nearestDistance;
        nearestDistance = distance;
        nearest = register;
      } else if (distance < secondDistance)
        secondDistance = distance;
    }

    return nearest;
  }

  private static int _Distance(ushort left, ushort right) {
    var red = (left >> 8 & 15) - (right >> 8 & 15);
    var green = (left >> 4 & 15) - (right >> 4 & 15);
    var blue = (left & 15) - (right & 15);
    return red * red + green * green + blue * blue;
  }

  private static ushort _PackColour(byte red, byte green, byte blue)
    => (ushort)((_ToNibble(red) << 8) | (_ToNibble(green) << 4) | _ToNibble(blue));

  private static int _ToNibble(byte value) => (value + 8) / 17;

  private static void _ExpandColour(ushort colour, Span<byte> destination) {
    destination[0] = (byte)((colour >> 8 & 15) * 17);
    destination[1] = (byte)((colour >> 4 & 15) * 17);
    destination[2] = (byte)((colour & 15) * 17);
  }
}
