using System;
using System.IO;

namespace FileFormat.AtariGraphics10;

/// <summary>Reads Atari Graphics 10 (GTIA 9-color) images from bytes, streams, or file paths.</summary>
public static class AtariGraphics10Reader {

  public static AtariGraphics10File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Graphics 10 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGraphics10File FromStream(Stream stream) {
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

  public static AtariGraphics10File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariGraphics10File.FileSize)
      throw new InvalidDataException($"Invalid Atari Graphics 10 data size: expected exactly {AtariGraphics10File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariGraphics10File.FileSize];
    data.AsSpan(0, AtariGraphics10File.FileSize).CopyTo(pixelData);

    return new AtariGraphics10File { PixelData = pixelData };
  }
}
