using System;
using System.IO;

namespace FileFormat.AtariGraphics11;

/// <summary>Reads Atari Graphics 11 (GTIA 16-luminance) images from bytes, streams, or file paths.</summary>
public static class AtariGraphics11Reader {

  public static AtariGraphics11File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Graphics 11 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGraphics11File FromStream(Stream stream) {
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

  public static AtariGraphics11File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariGraphics11File.FileSize)
      throw new InvalidDataException($"Invalid Atari Graphics 11 data size: expected exactly {AtariGraphics11File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariGraphics11File.FileSize];
    data.AsSpan(0, AtariGraphics11File.FileSize).CopyTo(pixelData);

    return new AtariGraphics11File { PixelData = pixelData };
  }
}
