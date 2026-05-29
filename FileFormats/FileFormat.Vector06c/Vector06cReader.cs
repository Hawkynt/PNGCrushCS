using System;
using System.IO;

namespace FileFormat.Vector06c;

/// <summary>Reads Vector-06C screen files from bytes, streams, or file paths.</summary>
public static class Vector06cReader {

  public static Vector06cFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Vector06c file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Vector06cFile FromStream(Stream stream) {
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

  public static Vector06cFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != Vector06cFile.FileSize)
      throw new InvalidDataException($"Invalid Vector06c data size: expected exactly {Vector06cFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[Vector06cFile.FileSize];
    data.Slice(0, Vector06cFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
    }

  public static Vector06cFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != Vector06cFile.FileSize)
      throw new InvalidDataException($"Invalid Vector06c data size: expected exactly {Vector06cFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[Vector06cFile.FileSize];
    data.AsSpan(0, Vector06cFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
