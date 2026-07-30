using System;

namespace FileFormat.Core;

/// <summary>Averages two frames, which is what a display alternating between them looks like.</summary>
/// <remarks>
/// Several machines with too few colours show two pictures on alternate television fields and let
/// the eye do the mixing — the Atari with APAC and McPainter, the ST with Duo, the Timex with its
/// gigascreen mode. The trick is the same everywhere, so the averaging lives here rather than with
/// any one of them.
/// <para/>
/// The average rounds down. That is not an arbitrary choice: it is what the reference decoder
/// produces, and rounding the other way puts every blended channel one above it.
/// </remarks>
public static class FrameBlend {

  /// <summary>Averages two equally sized RGB frames channel by channel.</summary>
  public static byte[] Average(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    var blended = new byte[first.Length];
    for (var i = 0; i < blended.Length; ++i)
      blended[i] = (byte)((first[i] & second[i]) + (((first[i] ^ second[i]) >> 1) & 0x7F));

    return blended;
  }
}
