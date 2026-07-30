using System;
using System.IO;

namespace FileFormat.VerticalHiresInterlace;

/// <summary>Reads Vertical Hires Interlace pictures from bytes, streams, or file paths.</summary>
public static class VerticalHiresInterlaceReader {

  public static VerticalHiresInterlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VerticalHiresInterlaceFile FromStream(Stream stream) {
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

  public static VerticalHiresInterlaceFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length == VerticalHiresInterlaceFile.PlainFileSize)
      return new() {
        Data = data.ToArray(),
        FirstBitmapOffset = 2,
        SecondBitmapOffset = 8194,
        VideoMatrixOffset = 16386,
      };

    var unpacked = _TryUnpack(data)
      ?? throw new InvalidDataException($"Not a Vertical Hires Interlace picture: {data.Length} bytes that do not unpack.");

    return new() {
      Data = unpacked,
      FirstBitmapOffset = 0,
      SecondBitmapOffset = 8192,
      VideoMatrixOffset = 16384,
    };
  }

  /// <summary>
  /// Unpacks the run-length encoding, which alternates between literal runs and repeated bytes.
  /// </summary>
  /// <remarks>
  /// A command byte of 0 introduces a run of literals and 1 a repeated value; in both cases a count
  /// of zero means 256, since a count of zero would otherwise be useless and 256 does not fit in a
  /// byte. Anything else as a command means this is not the format.
  /// </remarks>
  private static byte[]? _TryUnpack(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      return null;

    var unpacked = new byte[VerticalHiresInterlaceFile.UnpackedSize];
    var source = 2;

    for (var target = 0; target < unpacked.Length;) {
      if (source >= data.Length)
        return null;

      var command = data[source++];
      if (command > 1 || source >= data.Length)
        return null;

      var count = data[source++];
      var repeat = count == 0 ? 256 : count;

      if (command == 0) {
        if (source + repeat > data.Length)
          return null;

        while (repeat-- > 0 && target < unpacked.Length)
          unpacked[target++] = data[source++];
      } else {
        if (source >= data.Length)
          return null;

        var value = data[source++];
        while (repeat-- > 0 && target < unpacked.Length)
          unpacked[target++] = value;
      }
    }

    return unpacked;
  }

  public static VerticalHiresInterlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
