using System;

namespace FileFormat.CrackArt;

/// <summary>The header at the start of every CrackArt file.</summary>
/// <remarks>
/// Layout: the ASCII tag <c>CA</c>, a compression flag, a resolution byte, then the ST palette as
/// big-endian 16-bit entries. How many palette entries there are — and therefore where the bitmap
/// starts — follows from the resolution: low res carries 16 entries and starts the bitmap at 36,
/// medium carries 4 and starts at 12, and high res is monochrome with no palette and starts at 4.
/// </remarks>
public static class CrackArtHeader {

  /// <summary>ASCII tag every CrackArt file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => "CA"u8;

  /// <summary>Offset of the palette, immediately after the tag and the two flag bytes.</summary>
  public const int PaletteOffset = 4;

  /// <summary>Byte offset at which the bitmap begins for the given resolution.</summary>
  public static int GetDataOffset(CrackArtResolution resolution) => resolution switch {
    CrackArtResolution.Low => 36,
    CrackArtResolution.Medium => 12,
    CrackArtResolution.High => 4,
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown CrackArt resolution.")
  };

  /// <summary>Number of palette entries the given resolution stores.</summary>
  public static int GetPaletteEntryCount(CrackArtResolution resolution) => resolution switch {
    CrackArtResolution.Low => 16,
    CrackArtResolution.Medium => 4,
    CrackArtResolution.High => 0,
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown CrackArt resolution.")
  };

  /// <summary>Reads the tag and flag bytes; returns <c>false</c> when this is not a CrackArt header.</summary>
  public static bool TryRead(ReadOnlySpan<byte> data, out bool isCompressed, out CrackArtResolution resolution) {
    isCompressed = false;
    resolution = CrackArtResolution.Low;
    if (data.Length < 8 || !data[..Signature.Length].SequenceEqual(Signature))
      return false;

    if (data[2] > 1 || data[3] > 2)
      return false;

    isCompressed = data[2] == 1;
    resolution = (CrackArtResolution)data[3];
    return true;
  }

  /// <summary>Writes the tag, flags and palette into <paramref name="destination"/>.</summary>
  public static void Write(Span<byte> destination, bool isCompressed, CrackArtResolution resolution, short[] palette) {
    ArgumentNullException.ThrowIfNull(palette);
    Signature.CopyTo(destination);
    destination[2] = (byte)(isCompressed ? 1 : 0);
    destination[3] = (byte)resolution;

    var entries = Math.Min(GetPaletteEntryCount(resolution), palette.Length);
    for (var i = 0; i < entries; ++i) {
      destination[PaletteOffset + i * 2] = (byte)(palette[i] >> 8);
      destination[PaletteOffset + i * 2 + 1] = (byte)palette[i];
    }
  }

  /// <summary>Reads the palette for the given resolution.</summary>
  public static short[] ReadPalette(ReadOnlySpan<byte> data, CrackArtResolution resolution) {
    var count = GetPaletteEntryCount(resolution);
    var palette = new short[count];
    for (var i = 0; i < count; ++i)
      palette[i] = (short)((data[PaletteOffset + i * 2] << 8) | data[PaletteOffset + i * 2 + 1]);

    return palette;
  }
}
