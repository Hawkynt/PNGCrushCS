using System;
using System.IO;

namespace FileFormat.WigmoreArtist;

/// <summary>Reads Wigmore Artist (.wig) files from bytes, streams, or file paths.</summary>
public static class WigmoreArtistReader {

  public static WigmoreArtistFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Wigmore Artist file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WigmoreArtistFile FromStream(Stream stream) {
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

  public static WigmoreArtistFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < WigmoreArtistFile.LoadAddressSize + WigmoreArtistFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Wigmore Artist file (expected at least {WigmoreArtistFile.LoadAddressSize + WigmoreArtistFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - WigmoreArtistFile.LoadAddressSize];
    data.AsSpan(WigmoreArtistFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
