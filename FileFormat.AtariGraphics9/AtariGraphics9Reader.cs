using System;
using System.IO;

namespace FileFormat.AtariGraphics9;

/// <summary>Reads Atari Graphics 9 (GTIA 16-shade) images from bytes, streams, or file paths.</summary>
public static class AtariGraphics9Reader {

  public static AtariGraphics9File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Graphics 9 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGraphics9File FromStream(Stream stream) {
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

  public static AtariGraphics9File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != AtariGraphics9File.FileSize)
      throw new InvalidDataException($"Invalid Atari Graphics 9 data size: expected exactly {AtariGraphics9File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariGraphics9File.FileSize];
    data.Slice(0, AtariGraphics9File.FileSize).CopyTo(pixelData);

    return new AtariGraphics9File { PixelData = pixelData };
    }

  public static AtariGraphics9File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariGraphics9File.FileSize)
      throw new InvalidDataException($"Invalid Atari Graphics 9 data size: expected exactly {AtariGraphics9File.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariGraphics9File.FileSize];
    data.AsSpan(0, AtariGraphics9File.FileSize).CopyTo(pixelData);

    return new AtariGraphics9File { PixelData = pixelData };
  }
}
