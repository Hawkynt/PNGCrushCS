using System;
using System.Buffers.Binary;

namespace FileFormat.ArtDirector;

/// <summary>Writes the published Atari ST Art Director screen-first format.</summary>
public static class ArtDirectorWriter {

  /// <summary>Serializes exactly 32,000 screen bytes followed by sixteen 16-word palettes.</summary>
  public static byte[] ToBytes(ArtDirectorFile file) {
    ArtDirectorFile.ValidateForWrite(file, nameof(file));

    var result = new byte[ArtDirectorFile.ExpectedFileSize];
    file.PixelData.CopyTo(result, 0);

    var cycle = new short[ArtDirectorFile.PaletteCycleWords];
    if (file.PaletteCycle is null) {
      for (var slot = 0; slot < ArtDirectorFile.StoredPaletteCount; ++slot)
        file.Palette.CopyTo(cycle, slot * ArtDirectorFile.ColorsPerPalette);
    } else
      file.PaletteCycle.CopyTo(cycle, 0);

    // Palette remains the public displayed-palette API. If a caller edits it after reading a file,
    // update just that slot while preserving the other fifteen animation palettes.
    file.Palette.CopyTo(cycle, ArtDirectorFile.DisplayedPaletteIndex * ArtDirectorFile.ColorsPerPalette);

    var output = result.AsSpan(ArtDirectorFile.PlanarDataSize);
    for (var i = 0; i < cycle.Length; ++i)
      BinaryPrimitives.WriteInt16BigEndian(output[(i * 2)..], cycle[i]);

    return result;
  }
}
