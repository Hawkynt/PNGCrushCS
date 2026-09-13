using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Apac3;

/// <summary>Reads APAC 3 pictures from bytes, streams, or file paths.</summary>
public static class Apac3Reader {

  public static Apac3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("APAC 3 picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Apac3File FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static Apac3File FromSpan(ReadOnlySpan<byte> data) {
    if (SfdnDecompressor.IsSfdn(data)) {
      var unpacked = SfdnDecompressor.TryUnpack(data, SfdnDecompressor.UnpackedLength(data))
        ?? throw new InvalidDataException("Not an APAC 3 picture: the SFDN data does not unpack.");

      return FromSpan((ReadOnlySpan<byte>)unpacked);
    }

    // The length is the only header, and it says where the hue halves begin: the shorter files put
    // them straight after the luminance ones, the longer leaves a gap the picture does not use.
    var hueOffset = data.Length switch {
      15360 or 15362 => 7680,
      15872 => 8192,
      _ => throw new InvalidDataException($"An APAC 3 picture is 15360, 15362 or 15872 bytes, got {data.Length}."),
    };

    return new() { Data = data.ToArray(), HueOffset = hueOffset };
  }

  public static Apac3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
