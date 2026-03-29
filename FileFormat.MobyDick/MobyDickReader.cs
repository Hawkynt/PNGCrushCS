using System;
using System.IO;

namespace FileFormat.MobyDick;

/// <summary>Reads Moby Dick paint files from bytes, streams, or file paths.</summary>
public static class MobyDickReader {

  public static MobyDickFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Moby Dick file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MobyDickFile FromStream(Stream stream) {
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

  public static MobyDickFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MobyDickFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Moby Dick file (expected {MobyDickFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != MobyDickFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Moby Dick file size (expected {MobyDickFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    // Palette (768 bytes)
    var palette = new byte[MobyDickFile.PaletteDataSize];
    data.AsSpan(offset, MobyDickFile.PaletteDataSize).CopyTo(palette.AsSpan(0));
    offset += MobyDickFile.PaletteDataSize;

    // Pixel data (64000 bytes)
    var pixelData = new byte[MobyDickFile.PixelDataSize];
    data.AsSpan(offset, MobyDickFile.PixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
