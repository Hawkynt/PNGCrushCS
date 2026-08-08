using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.X11Puzzle;

/// <summary>Reads jigsaw puzzle pictures from bytes, streams, or file paths.</summary>
public static class X11PuzzleReader {

  public static X11PuzzleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Puzzle picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static X11PuzzleFile FromStream(Stream stream) {
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

  public static X11PuzzleFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= X11PuzzleFile.PixelOffset)
      throw new InvalidDataException($"Data too small for a puzzle picture: got {data.Length} bytes.");

    var width = BinaryPrimitives.ReadUInt32BigEndian(data);
    var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);

    if (width is 0 or > 8192 || height is 0 or > 8192)
      throw new InvalidDataException($"Invalid puzzle size: {width}x{height}.");

    // No signature, so the size stated has to account for the whole file.
    var needed = X11PuzzleFile.PixelOffset + (long)width * height;
    if (data.Length != needed)
      throw new InvalidDataException($"A {width}x{height} puzzle picture is {needed} bytes, got {data.Length}.");

    return new() {
      Width = (int)width,
      Height = (int)height,
      Reserved = data[8],
      Palette = data.Slice(X11PuzzleFile.HeaderSize, X11PuzzleFile.PaletteSize).ToArray(),
      PixelData = data[X11PuzzleFile.PixelOffset..].ToArray(),
    };
  }

  public static X11PuzzleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
