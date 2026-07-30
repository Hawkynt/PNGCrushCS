using System;
using System.IO;

namespace FileFormat.Atari8Player;

/// <summary>Reads AtariTools-800 players from bytes, streams, or file paths.</summary>
public static class Atari8PlayerReader {

  public static Atari8PlayerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Player not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Atari8PlayerFile FromStream(Stream stream) {
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

  public static Atari8PlayerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != Atari8PlayerFile.FileSize)
      throw new InvalidDataException($"A player is {Atari8PlayerFile.FileSize} bytes, got {data.Length}.");

    var shape = new byte[Atari8PlayerFile.Height];
    data[Atari8PlayerFile.ShapeOffset..].CopyTo(shape);

    return new() { Color = data[0], Shape = shape };
  }

  public static Atari8PlayerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
