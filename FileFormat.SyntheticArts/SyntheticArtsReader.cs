using System;
using System.IO;

namespace FileFormat.SyntheticArts;

/// <summary>Reads Synthetic Arts (.srt) files from bytes, streams, or file paths.</summary>
public static class SyntheticArtsReader {

  public static SyntheticArtsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Synthetic Arts file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SyntheticArtsFile FromStream(Stream stream) {
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

  public static SyntheticArtsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SyntheticArtsFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Synthetic Arts file (expected {SyntheticArtsFile.FileSize} bytes, got {data.Length}).");

    var header = SyntheticArtsHeader.ReadFrom(data.AsSpan());
    var palette = header.GetPaletteArray();

    var pixelData = new byte[32000];
    data.AsSpan(SyntheticArtsHeader.StructSize, 32000).CopyTo(pixelData);

    return new SyntheticArtsFile {
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
