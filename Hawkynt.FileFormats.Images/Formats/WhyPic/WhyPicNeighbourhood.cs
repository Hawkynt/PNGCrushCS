using System;

namespace FileFormat.WhyPic;

/// <summary>Where the eight context dots of a WhyPic pixel are, and the palette's own model.</summary>
internal static class WhyPicNeighbourhood {

  /// <summary>
  /// Reads the eight neighbours of the dot at <paramref name="at"/> from the dots decoded so far.
  /// </summary>
  /// <remarks>
  /// The picture is one run of dots, so a neighbour is simply a fixed distance back along it, and a
  /// distance that reaches before the start lands on the first dot. Nothing that has not been
  /// decoded yet is ever meant to be read — except in a picture one dot wide, where "above and to
  /// the right" is the dot itself; there the decoder sees whatever its buffer holds, which is zero,
  /// and an encoder has to see the same to be read back.
  /// </remarks>
  public static void Gather(byte[] dots, int at, int width, Span<byte> neighbours) {
    neighbours[0] = _At(dots, at - 2 * width);
    neighbours[1] = _At(dots, at - width - 1);
    neighbours[2] = _At(dots, at - width);
    neighbours[3] = _At(dots, at - width + 1);
    neighbours[4] = _At(dots, at - 4);
    neighbours[5] = _At(dots, at - 3);
    neighbours[6] = _At(dots, at - 2);
    neighbours[7] = _At(dots, at - 1);
  }

  private static byte _At(byte[] dots, int index) => dots[index < 0 ? 0 : index];
}

/// <summary>The palette's model: an even split over byte values mixed with the counts of the bytes before.</summary>
internal static class WhyPicPaletteModel {

  /// <summary>The interval of byte <paramref name="value"/> as the <paramref name="index"/>th palette byte.</summary>
  /// <param name="lambda">The weight of the even split.</param>
  /// <param name="index">How many palette bytes came before.</param>
  /// <param name="value">The byte being coded.</param>
  /// <param name="below">How many earlier bytes were smaller than <paramref name="value"/>.</param>
  /// <param name="count">How many earlier bytes were equal to it.</param>
  public static (int Low, int High) Interval(double lambda, int index, int value, int below, int count) {
    double low, high;
    if (index == 0) {
      // Nothing has been counted yet, so the first byte is spread evenly.
      low = (double)value / 256;
      high = (double)(value + 1) / 256;
    } else {
      low = lambda * value / 256 + (-lambda + 1) * below / index;
      high = lambda * (value + 1) / 256 + (-lambda + 1) * (below + count) / index;
    }

    return ((int)(low * WhyPicContextModel.ProbabilityScale), (int)(high * WhyPicContextModel.ProbabilityScale));
  }
}
