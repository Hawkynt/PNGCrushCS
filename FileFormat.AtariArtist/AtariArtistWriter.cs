using System;

namespace FileFormat.AtariArtist;

/// <summary>Assembles Atari Artist file bytes from an <see cref="AtariArtistFile"/>.</summary>
public static class AtariArtistWriter {

  public static byte[] ToBytes(AtariArtistFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariArtistFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariArtistFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
