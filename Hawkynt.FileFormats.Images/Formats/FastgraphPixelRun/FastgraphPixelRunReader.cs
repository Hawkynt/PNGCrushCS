using System;
using System.IO;

namespace FileFormat.FastgraphPixelRun;

/// <summary>Reads Fastgraph pixel run pictures from bytes, streams, or file paths.</summary>
public static class FastgraphPixelRunReader {

  public static FastgraphPixelRunFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fastgraph picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FastgraphPixelRunFile FromStream(Stream stream) {
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

  public static FastgraphPixelRunFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FastgraphPixelRunFile.HeaderSize || !data[..16].SequenceEqual(FastgraphPixelRunFile.Magic))
      throw new InvalidDataException("Not a Fastgraph picture: it does not open with FASTGRAF.");

    // Every byte of this header is written as a word with the high half zero, the name included. A
    // high half that is not zero says these twenty-six bytes are somebody else's.
    for (var at = 17; at < FastgraphPixelRunFile.HeaderSize; at += 2)
      if (data[at] != 0)
        throw new InvalidDataException($"A Fastgraph header byte at {at} is not the zero half of a word.");

    var width = data[16] | (data[18] << 8);
    var height = data[20] | (data[22] << 8);
    ++width;
    ++height;

    if (width is < 1 or > FastgraphPixelRunFile.MaxDimension || height is < 1 or > FastgraphPixelRunFile.MaxDimension)
      throw new InvalidDataException($"A Fastgraph picture states a size of {width}x{height}.");

    var body = data[FastgraphPixelRunFile.HeaderSize..];
    if ((body.Length & 1) != 0)
      throw new InvalidDataException("A Fastgraph run stream is pairs of bytes and this one has an odd number.");

    var pixels = new byte[width * height];

    // The runs fill the bottom row first and run left to right within a row, so they are unwound into
    // a row from the bottom up. Reaching the stated size on the last pair is what says the header and
    // the stream are describing the same picture.
    var row = height - 1;
    var column = 0;
    for (var at = 0; at + 1 < body.Length; at += 2) {
      var colour = body[at];
      var count = body[at + 1];
      if (count == 0)
        throw new InvalidDataException($"A Fastgraph run of no pixels at {FastgraphPixelRunFile.HeaderSize + at}.");

      while (count > 0) {
        if (row < 0)
          throw new InvalidDataException("A Fastgraph run stream covers more pixels than the stated size holds.");

        var take = Math.Min((int)count, width - column);
        pixels.AsSpan(row * width + column, take).Fill(colour);
        column += take;
        count -= (byte)take;
        if (column < width)
          continue;

        column = 0;
        --row;
      }
    }

    if (row >= 0)
      throw new InvalidDataException($"A Fastgraph run stream covers {(height - 1 - row) * width + column} pixels and the header states {width * height}.");

    return new() { Width = width, Height = height, Pixels = pixels };
  }

  public static FastgraphPixelRunFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
