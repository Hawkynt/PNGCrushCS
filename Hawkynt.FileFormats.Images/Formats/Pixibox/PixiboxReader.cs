using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Pixibox;

/// <summary>Reads Pixibox pictures from bytes, streams, or file paths.</summary>
public static class PixiboxReader {

  public static PixiboxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pixibox file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PixiboxFile FromStream(Stream stream) {
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

  public static PixiboxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PixiboxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PixiboxFile.PixelDataOffset)
      throw new InvalidDataException(
        $"Data too small for a Pixibox picture (minimum {PixiboxFile.PixelDataOffset} bytes, got {data.Length}).");

    if (!data[..PixiboxFile.Signature.Length].SequenceEqual(PixiboxFile.Signature))
      throw new InvalidDataException("Not a Pixibox picture: the twelve bytes it opens with are not the format's.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[PixiboxFile.WidthOffset..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[PixiboxFile.HeightOffset..]);
    if (width < 1 || height < 1)
      throw new InvalidDataException($"A Pixibox picture states a size of {width}x{height}.");

    var pixels = new byte[width * height * 3];
    var at = PixiboxFile.PixelDataOffset;

    // Rows run from the bottom of the picture upwards, which is what XnView returns for a file built
    // this way; the same file read from the top comes back upside down.
    for (var row = 0; row < height; ++row) {
      var y = height - 1 - row;
      var x = 0;
      while (x < width) {
        if (at + PixiboxFile.RunSize > data.Length)
          throw new InvalidDataException(
            $"A Pixibox picture of {width}x{height} runs out of coded data in row {row} at column {x}.");

        var count = data[at];

        // Zero is not an empty run: it stands for the rest of the row.
        var run = count == 0 ? width - x : count;
        if (run > width - x)
          throw new InvalidDataException(
            $"A Pixibox run of {run} pixels at column {x} of row {row} reaches past the stated width {width}.");

        var red = data[at + 1];
        var green = data[at + 2];
        var blue = data[at + 3];
        at += PixiboxFile.RunSize;

        var to = (y * width + x) * 3;
        for (var i = 0; i < run; ++i, to += 3) {
          pixels[to] = red;
          pixels[to + 1] = green;
          pixels[to + 2] = blue;
        }

        x += run;
      }
    }

    return new() { Width = width, Height = height, PixelData = pixels };
  }
}
