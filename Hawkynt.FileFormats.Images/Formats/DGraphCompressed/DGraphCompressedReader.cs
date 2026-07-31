using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DGraphCompressed;

/// <summary>Reads compressed D-GRAPH pictures from bytes, streams, or file paths.</summary>
public static class DGraphCompressedReader {

  public static DGraphCompressedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DGraphCompressedFile FromStream(Stream stream) {
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

  public static DGraphCompressedFile FromSpan(ReadOnlySpan<byte> data) {
    var at = 0;
    var firstLength = _ParseLength(data, ref at);

    var paletteOffset = at;
    var firstEnd = paletteOffset + DGraphCompressedFile.PaletteSize + firstLength;
    if (firstEnd >= data.Length)
      throw new InvalidDataException("A D-GRAPH picture's first block accounts for the whole file.");

    var screens = new byte[DGraphCompressedFile.ScreenSize * 2];
    var rle = new AtariStCaRle(data, paletteOffset + DGraphCompressedFile.PaletteSize);
    rle.UnpackBlock(screens, 0, DGraphCompressedFile.ScreenSize, firstEnd);

    // The second length sits wherever the first block's stream stopped, not at its declared end.
    at = rle.Position;
    var secondLength = _ParseLength(data, ref at);
    if (at + secondLength != data.Length)
      throw new InvalidDataException("A D-GRAPH picture's second block does not account for the file.");

    rle.Position = at;
    rle.UnpackBlock(screens, DGraphCompressedFile.ScreenSize, DGraphCompressedFile.ScreenSize, data.Length);

    return new() {
      ScreenData = screens,
      Palette = data.Slice(paletteOffset, DGraphCompressedFile.PaletteSize).ToArray(),
    };
  }

  /// <summary>Reads a length written as decimal digits and closed by a carriage return.</summary>
  private static int _ParseLength(ReadOnlySpan<byte> data, ref int at) {
    var value = 0;
    var digits = 0;

    while (at < data.Length && data[at] >= '0' && data[at] <= '9') {
      value = value * 10 + (data[at++] - '0');
      if (++digits > 5)
        throw new InvalidDataException("Not a D-GRAPH picture: a length runs on.");
    }

    if (digits == 0 || value < 10 || value > 32000)
      throw new InvalidDataException($"Not a D-GRAPH picture: a block of {value} bytes.");

    if (at + 1 >= data.Length || data[at] != '\r' || data[at + 1] != '\n')
      throw new InvalidDataException("Not a D-GRAPH picture: a length is not closed.");

    at += 2;

    return value;
  }

  public static DGraphCompressedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
