using System;
using System.IO;

namespace FileFormat.Atari8Missile;

/// <summary>Reads AtariTools-800 missiles from bytes, streams, or file paths.</summary>
public static class Atari8MissileReader {

  public static Atari8MissileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Missile not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Atari8MissileFile FromStream(Stream stream) {
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

  public static Atari8MissileFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != Atari8MissileFile.FileSize && data.Length != Atari8MissileFile.PaddedFileSize)
      throw new InvalidDataException(
        $"A missile is {Atari8MissileFile.FileSize} or {Atari8MissileFile.PaddedFileSize} bytes, got {data.Length}.");

    var shape = new byte[Atari8MissileFile.Height / Atari8MissileFile.RowsPerByte];
    data.Slice(Atari8MissileFile.ShapeOffset, shape.Length).CopyTo(shape);

    return new() {
      Color = data[0],
      Shape = shape,
      IsPadded = data.Length == Atari8MissileFile.PaddedFileSize,
    };
  }

  public static Atari8MissileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
