using System;

namespace FileFormat.Ccitt;

/// <summary>Turns a fax stream whose bits run from the bottom of each byte into one that runs from the top.</summary>
/// <remarks>
/// T.4 and T.6 say nothing about which end of a byte the first bit of the coding sits at; TIFF calls
/// the choice <c>FillOrder</c> and both values occur in the wild. The decoders here read from the top
/// bit down, which is the common order, so a stream written the other way round has to have each of
/// its bytes turned over before it can be decoded at all — not shifted, reversed, because the codes
/// straddle byte boundaries.
/// <para/>
/// Three of the formats recovered from XnView's converter store their coding this way — Skantek,
/// Ricoh Fax and SmartFax, and Xionics SMP when it is coded rather than raw. In the converter the
/// choice is a word in the fax context that picks between an identity byte table and a bit-reversal
/// one, which is exactly this.
/// </remarks>
internal static class CcittFillOrder {

  private static readonly byte[] _Reversed = _BuildReversed();

  private static byte[] _BuildReversed() {
    var table = new byte[256];
    for (var i = 0; i < 256; ++i) {
      var value = 0;
      for (var bit = 0; bit < 8; ++bit)
        if ((i & (1 << bit)) != 0)
          value |= 0x80 >> bit;

      table[i] = (byte)value;
    }

    return table;
  }

  /// <summary>Returns a copy of the coding with every byte's bits turned over.</summary>
  public static byte[] Reverse(ReadOnlySpan<byte> coded) {
    var result = new byte[coded.Length];
    for (var i = 0; i < coded.Length; ++i)
      result[i] = _Reversed[coded[i]];

    return result;
  }
}
