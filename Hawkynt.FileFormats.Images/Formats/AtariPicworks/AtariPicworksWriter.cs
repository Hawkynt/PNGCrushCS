using System;

namespace FileFormat.AtariPicworks;

/// <summary>Assembles a Picworks picture from an <see cref="AtariPicworksFile"/>.</summary>
public static class AtariPicworksWriter {

  /// <summary>Writes the screen with no runs at all, which the format permits.</summary>
  /// <remarks>
  /// The count word says how many pairs of runs follow; zero of them is legal, and what remains is
  /// then the plain screen. That produces a larger file than a packer would, and an exactly correct
  /// one — the packing is a size optimisation rather than part of the format's meaning, and a
  /// reader cannot tell the difference except by the length.
  /// </remarks>
  public static byte[] ToBytes(AtariPicworksFile file) {
    var screen = file.ScreenData ?? [];
    var result = new byte[AtariPicworksFile.CountsOffset + AtariPicworksFile.ScreenSize];

    // Bytes 0 and 1 are the pair count, left at zero; 2 and 3 sit inside the counts area and are
    // never read.
    screen
      .AsSpan(0, Math.Min(screen.Length, AtariPicworksFile.ScreenSize))
      .CopyTo(result.AsSpan(AtariPicworksFile.CountsOffset));

    return result;
  }
}
