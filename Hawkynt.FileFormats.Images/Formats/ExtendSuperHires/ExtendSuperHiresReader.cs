using System;
using System.IO;

namespace FileFormat.ExtendSuperHires;

/// <summary>Reads Extend Super Hires Interlace Editor pictures from bytes, streams, or file paths.</summary>
public static class ExtendSuperHiresReader {

  public static ExtendSuperHiresFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ExtendSuperHiresFile FromStream(Stream stream) {
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

  public static ExtendSuperHiresFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 5)
      throw new InvalidDataException($"Not an Extend Super Hires picture: {data.Length} bytes.");

    // The third byte says whether the rest is packed, and a zero there fixes the file's length.
    if (data[2] == 0) {
      if (data.Length != ExtendSuperHiresFile.UnpackedFileSize)
        throw new InvalidDataException(
          $"An unpacked picture is {ExtendSuperHiresFile.UnpackedFileSize} bytes, got {data.Length}.");

      return new() { Data = data.ToArray() };
    }

    return new() { Data = _Unpack(data) };
  }

  /// <summary>
  /// Unpacks the run-length encoding, whose command byte's top bit chooses between a repeated value
  /// and a run of literals.
  /// </summary>
  /// <remarks>
  /// Both counts are what the seven low bits say and not one more, so a command of 0 or 128 does
  /// nothing at all. Spending two encodings on nothing is what keeps the count and the flag in one
  /// byte with no bias to undo.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var unpacked = new byte[ExtendSuperHiresFile.UnpackedSize];
    var at = 3;

    for (var target = 3; target < unpacked.Length;) {
      if (at >= data.Length)
        throw new InvalidDataException("A packed Extend Super Hires picture ends before its picture does.");

      var command = data[at++];
      var count = command & 127;

      if (command < 128) {
        if (at + count > data.Length)
          throw new InvalidDataException("A run of literals runs past the end of the file.");

        while (count-- > 0 && target < unpacked.Length)
          unpacked[target++] = data[at++];

        continue;
      }

      if (at >= data.Length)
        throw new InvalidDataException("A repeated run has no value.");

      var value = data[at++];
      while (count-- > 0 && target < unpacked.Length)
        unpacked[target++] = value;
    }

    return unpacked;
  }

  public static ExtendSuperHiresFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
