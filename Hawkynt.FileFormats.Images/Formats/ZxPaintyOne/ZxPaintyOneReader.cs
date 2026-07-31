using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxPaintyOne;

/// <summary>Reads ZXpaintyONE pictures from bytes, streams, or file paths.</summary>
public static class ZxPaintyOneReader {

  public static ZxPaintyOneFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxPaintyOneFile FromStream(Stream stream) {
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

  public static ZxPaintyOneFile FromSpan(ReadOnlySpan<byte> data) {
    var screen = new byte[Zx81Graphics.ScreenSize];
    var at = 0;

    for (var i = 0; i < screen.Length; ++i) {
      var high = _Digit(data, at++);
      var low = _Digit(data, at++);
      if (high < 0 || low < 0)
        throw new InvalidDataException($"Not a ZXpaintyONE picture: code {i} is not two hexadecimal digits.");

      screen[i] = (byte)((high << 4) | low);
    }

    return new() { Screen = screen };
  }

  private static int _Digit(ReadOnlySpan<byte> data, int at) {
    if (at >= data.Length)
      return -1;

    var c = data[at];

    return c switch {
      >= (byte)'0' and <= (byte)'9' => c - '0',
      >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
      >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
      _ => -1,
    };
  }

  public static ZxPaintyOneFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
