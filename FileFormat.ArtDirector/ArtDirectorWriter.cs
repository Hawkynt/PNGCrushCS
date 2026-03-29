using System;
using System.Buffers.Binary;

namespace FileFormat.ArtDirector;

/// <summary>Assembles Atari ST Art Director image bytes from an ArtDirectorFile.</summary>
public static class ArtDirectorWriter {

  public static byte[] ToBytes(ArtDirectorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ArtDirectorFile.ExpectedFileSize];
    var span = result.AsSpan();

    BinaryPrimitives.WriteInt16BigEndian(span, file.Resolution);

    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(span[(ArtDirectorFile.PaletteOffset + i * 2)..], i < file.Palette.Length ? file.Palette[i] : (short)0);

    // Bytes 34-127 are reserved/padding (left as zero)
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, ArtDirectorFile.PlanarDataSize)).CopyTo(result.AsSpan(ArtDirectorFile.HeaderSize));

    return result;
  }
}
