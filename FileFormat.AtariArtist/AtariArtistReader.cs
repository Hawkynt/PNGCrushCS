using System;
using System.IO;

namespace FileFormat.AtariArtist;

/// <summary>Reads Atari Artist files from bytes, streams, or file paths.</summary>
public static class AtariArtistReader {

  public static AtariArtistFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Artist file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariArtistFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AtariArtistFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariArtistFile.ExpectedFileSize)
      throw new InvalidDataException($"Atari Artist file must be exactly {AtariArtistFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariArtistFile.ExpectedFileSize];
    data.AsSpan(0, AtariArtistFile.ExpectedFileSize).CopyTo(pixelData);

    return new AtariArtistFile { PixelData = pixelData };
  }
}
