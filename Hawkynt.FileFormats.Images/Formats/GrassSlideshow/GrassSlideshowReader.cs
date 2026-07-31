using System;
using System.IO;

namespace FileFormat.GrassSlideshow;

/// <summary>Reads Grass' Slideshow pictures from bytes, streams, or file paths.</summary>
public static class GrassSlideshowReader {

  public static GrassSlideshowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GrassSlideshowFile FromStream(Stream stream) {
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

  public static GrassSlideshowFile FromSpan(ReadOnlySpan<byte> data) {
    var screen = new byte[GrassSlideshowFile.ScreenSize];
    var at = _Unpack(data, screen);

    // The byte after the picture names a register set rather than carrying one.
    var named = at < data.Length ? data[at] : -1;

    var registers = named switch {
      52 or 53 => (byte[])[0, 52,
        data.Length == GrassSlideshowFile.ShortFileSize ? (byte)56 : (byte)200,
        data.Length == GrassSlideshowFile.ShortFileSize ? (byte)60 : (byte)124],
      81 => [164, 81, 185, 124],
      228 => [0, 228, 200, 190],
      4 => [6, 4, 0, 10],
      48 => [14, 48, 199, 123],
      116 => [0, 116, 88, 126],
      _ => [0, 4, 8, 12],
    };

    return new() { ScreenData = screen, Registers = registers };
  }

  /// <summary>
  /// Unpacks the run-length encoding and returns where the stream stopped.
  /// </summary>
  /// <remarks>
  /// A command byte is a count of literals, unless it is zero — then a value and a count follow, in
  /// that order. Spending the zero on the repeated run rather than on a literal one is what keeps a
  /// single literal byte to a two-byte cost instead of three.
  /// </remarks>
  private static int _Unpack(ReadOnlySpan<byte> data, Span<byte> screen) {
    var at = 0;

    for (var target = 0; target < screen.Length;) {
      if (at >= data.Length)
        throw new InvalidDataException("A Grass' Slideshow picture ends before its picture does.");

      var command = data[at++];
      if (command == 0) {
        if (at + 1 >= data.Length)
          throw new InvalidDataException("A repeated run has no value or no count.");

        var value = data[at++];
        var count = data[at++];
        while (count-- > 0 && target < screen.Length)
          screen[target++] = value;

        continue;
      }

      if (at + command > data.Length)
        throw new InvalidDataException("A run of literals runs past the end of the file.");

      while (command-- > 0 && target < screen.Length)
        screen[target++] = data[at++];
    }

    return at;
  }

  public static GrassSlideshowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
