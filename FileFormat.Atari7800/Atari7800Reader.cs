using System;
using System.IO;

namespace FileFormat.Atari7800;

/// <summary>Parses atari 7800 maria screen dump from raw bytes.</summary>
public static class Atari7800Reader {

  public static Atari7800File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Atari7800File FromStream(Stream stream) {
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

  public static Atari7800File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Atari7800File.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected {Atari7800File.FileSize}.");

    var pixelData = new byte[Atari7800File.ImageWidth * Atari7800File.ImageHeight];
    data.Slice(0, pixelData.Length).CopyTo(pixelData.AsSpan(0));

    return new Atari7800File { PixelData = pixelData };
    }

  public static Atari7800File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Atari7800File.FileSize)
      throw new InvalidDataException($"Data too small: {data.Length} bytes, expected {Atari7800File.FileSize}.");

    var pixelData = new byte[Atari7800File.ImageWidth * Atari7800File.ImageHeight];
    data.AsSpan(0, pixelData.Length).CopyTo(pixelData.AsSpan(0));

    return new Atari7800File { PixelData = pixelData };
  }
}
