using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.ArtDirector;

/// <summary>Reads Atari ST Art Director images from bytes, streams, or file paths.</summary>
public static class ArtDirectorReader {

  /// <summary>Reads an Art Director picture from disk.</summary>
  public static ArtDirectorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Art Director file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads an Art Director picture from the current stream position through end-of-stream.</summary>
  public static ArtDirectorFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var length = checked((int)(stream.Length - stream.Position));
      if (length != ArtDirectorFile.ExpectedFileSize)
        throw new InvalidDataException($"Art Director files must be exactly {ArtDirectorFile.ExpectedFileSize} bytes; got {length}.");

      var data = new byte[length];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  /// <summary>Parses the published screen-first 32,512-byte Art Director layout.</summary>
  public static ArtDirectorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != ArtDirectorFile.ExpectedFileSize)
      throw new InvalidDataException($"Art Director files must be exactly {ArtDirectorFile.ExpectedFileSize} bytes; got {data.Length}.");

    var cycle = new short[ArtDirectorFile.PaletteCycleWords];
    var palettes = data[ArtDirectorFile.PlanarDataSize..];
    for (var i = 0; i < cycle.Length; ++i)
      cycle[i] = BinaryPrimitives.ReadInt16BigEndian(palettes[(i * 2)..]);

    var palette = new short[ArtDirectorFile.ColorsPerPalette];
    cycle.AsSpan(
      ArtDirectorFile.DisplayedPaletteIndex * ArtDirectorFile.ColorsPerPalette,
      ArtDirectorFile.ColorsPerPalette
    ).CopyTo(palette);

    return new ArtDirectorFile {
      Width = ArtDirectorFile.FixedWidth,
      Height = ArtDirectorFile.FixedHeight,
      Resolution = 0,
      Palette = palette,
      PaletteCycle = cycle,
      PixelData = data[..ArtDirectorFile.PlanarDataSize].ToArray(),
    };
  }

  /// <summary>Parses an Art Director byte array.</summary>
  public static ArtDirectorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
