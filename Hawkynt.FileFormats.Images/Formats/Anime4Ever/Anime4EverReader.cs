using System;
using System.IO;

namespace FileFormat.Anime4Ever;

/// <summary>Reads Anime 4ever pictures from bytes, streams, or file paths.</summary>
public static class Anime4EverReader {

  public static Anime4EverFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Anime4EverFile FromStream(Stream stream) {
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

  public static Anime4EverFile FromSpan(ReadOnlySpan<byte> data) => new() { Unpacked = _Unpack(data) };

  /// <summary>
  /// Unpacks the dictionary coding, whose flag bits are themselves packed two levels deep.
  /// </summary>
  /// <remarks>
  /// A flag says literal or reference, and the flags come eight to a byte — but whether the next
  /// eight need a byte of their own is itself a flag, from a second stream packed the same way. So
  /// a stretch of all-literals or all-references costs one bit per eight rather than eight, which
  /// matters on a picture whose runs are long.
  /// <para/>
  /// Both flag registers carry a sentinel bit below their eight, so a register is exhausted exactly
  /// when the low seven bits reach zero — the shift that consumes the last flag is also the test.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> source) {
    var data = source.ToArray();
    var unpacked = new byte[Anime4EverFile.UnpackedSize];
    var at = 0;
    int outerFlags = 0, innerFlags = 0;

    // The stream starts with nowhere to write; a command must name a destination first.
    var target = -1;

    int ReadFlag() {
      if ((innerFlags & 127) == 0) {
        if ((outerFlags & 127) == 0)
          outerFlags = (_Next(data, ref at) << 1) | 1;
        else
          outerFlags <<= 1;

        innerFlags = (outerFlags & 256) == 0 ? 1 : (_Next(data, ref at) << 1) | 1;
      } else
        innerFlags <<= 1;

      return (innerFlags >> 8) & 1;
    }

    void CopyByte() {
      var b = _Next(data, ref at);
      if (target < 0 || target >= Anime4EverFile.UnpackedSize)
        throw new InvalidDataException("An Anime 4ever picture writes outside the memory it describes.");

      unpacked[target++] = b;
    }

    void CopyBlock(int distance, int count) {
      if (target < 0 || target - distance < 0 || target + count > Anime4EverFile.UnpackedSize)
        throw new InvalidDataException("An Anime 4ever reference points outside the picture.");

      // Byte at a time, because a reference may overlap what it is still writing.
      for (var i = 0; i < count; ++i, ++target)
        unpacked[target] = unpacked[target - distance];
    }

    for (;;) {
      if (ReadFlag() == 0) {
        CopyByte();
        continue;
      }

      var command = _Next(data, ref at);

      if (command == 0) {
        // A destination, as an address in the machine's memory rather than an offset in the file.
        target = _Next(data, ref at) + (_Next(data, ref at) << 8) + 128 - 19984;
        CopyByte();
        continue;
      }

      if (command != 1) {
        CopyBlock(128 - (command >> 1), 2 + (command & 1));
        continue;
      }

      var count = _Next(data, ref at);
      if (count == 0)
        return unpacked;

      CopyBlock(1, count + 2);
    }
  }

  private static byte _Next(byte[] data, ref int at) {
    if (at >= data.Length)
      throw new InvalidDataException("An Anime 4ever picture ends before its picture does.");

    return data[at++];
  }

  public static Anime4EverFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
