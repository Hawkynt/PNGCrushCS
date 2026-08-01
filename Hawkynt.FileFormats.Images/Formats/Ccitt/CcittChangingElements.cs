using System;

namespace FileFormat.Ccitt;

/// <summary>
/// The changing elements of a scanline: the positions at which it switches colour.
/// </summary>
/// <remarks>
/// Group 3 two-dimensional and Group 4 both code a line as a series of answers to "where is the next
/// colour change, relative to where the line above changes", so this is the representation the codes
/// are actually about. Working in pixels instead invites a mistake that is easy to miss: the first
/// pixel of the opposite colour is not the same position as the first *change* to the opposite
/// colour, and on any line with more than one run the two differ.
///
/// A line always starts white — there is an imaginary white pixel off the left edge — so the changes
/// alternate from there, and the colour a change introduces is fixed by its index alone: even means
/// black, odd means white.
/// </remarks>
internal static class CcittChangingElements {

  /// <summary>Collects the positions where a packed 1bpp scanline changes colour.</summary>
  /// <param name="pixels">Packed pixel data, MSB first, a set bit meaning black.</param>
  /// <param name="offset">Byte offset of the scanline within <paramref name="pixels"/>.</param>
  /// <param name="width">Pixels in the scanline.</param>
  /// <param name="changes">Receives the positions; must hold at least <paramref name="width"/> entries.</param>
  /// <returns>How many positions were written.</returns>
  internal static int Collect(byte[] pixels, int offset, int width, int[] changes) {
    var count = 0;
    var previous = false; // the imaginary pixel off the left edge is white

    for (var x = 0; x < width; ++x) {
      var black = ((pixels[offset + (x >> 3)] >> (7 - (x & 7))) & 1) != 0;
      if (black == previous)
        continue;

      changes[count++] = x;
      previous = black;
    }

    return count;
  }

  /// <summary>
  /// Index of the first changing element strictly right of <paramref name="a0"/> whose colour is the
  /// opposite of the colour at a0, or <paramref name="count"/> when the line has no such change left.
  /// </summary>
  /// <remarks>
  /// Because the changes alternate from white, wanting a particular colour is the same as wanting a
  /// particular index parity, so this is a scan plus at most one step.
  /// </remarks>
  internal static int NextOfOppositeColour(int[] changes, int count, int a0, bool white) {
    var i = 0;
    while (i < count && changes[i] <= a0)
      ++i;

    if (((i & 1) == 0) != white)
      ++i;

    return i;
  }

  /// <summary>Paints the black runs of a line described by its changing elements.</summary>
  /// <remarks>Runs alternate from white, so black is what lies between an even-indexed change and the next.</remarks>
  internal static void Render(int[] changes, int count, byte[] pixels, int offset, int width) {
    for (var i = 0; i < count; i += 2) {
      var start = Math.Clamp(changes[i], 0, width);
      var end = Math.Clamp(i + 1 < count ? changes[i + 1] : width, 0, width);
      for (var x = start; x < end; ++x)
        pixels[offset + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
    }
  }
}
