using System;

namespace FileFormat.Core;

/// <summary>
/// The colour map an IFF picture from an Amiga carries, and how wide its channels really are.
/// </summary>
/// <remarks>
/// The machine's palette is four bits a channel. A colour map written by one of those machines puts
/// each value in the high nibble of its byte and leaves the low one empty, so a channel of 8 is
/// stored as 0x80 where the machine itself shows 0x88. Taken as it stands every colour comes out a
/// shade too dark and the brightest white reaches only 0xF0.
/// <para/>
/// The picture then looks right and no pixel of it is, which is the hardest kind of wrong to notice:
/// two real samples sat at 18 and 25 per cent of their pixels matching RECOIL while being
/// indistinguishable from it by eye.
/// <para/>
/// Three readers copied the map across untouched, each in its own words. The rule lives here now.
/// </remarks>
public static class AmigaColourMap {

  /// <summary>
  /// Repeats the high nibble of every channel where the map holds a four-bit palette.
  /// </summary>
  /// <remarks>
  /// A map whose low nibbles are every one of them zero is such a palette: colours chosen
  /// independently do not all land on a multiple of sixteen by chance. A map with anything in a low
  /// nibble is already eight bits a channel and is left exactly as it was.
  /// </remarks>
  /// <param name="colourMap">The map's bytes, three to an entry, widened in place.</param>
  public static void WidenIfFourBit(byte[]? colourMap) {
    if (colourMap == null)
      return;

    foreach (var value in colourMap)
      if ((value & 0x0F) != 0)
        return;

    for (var i = 0; i < colourMap.Length; ++i)
      colourMap[i] |= (byte)(colourMap[i] >> 4);
  }
}
