using System;
using System.IO;

namespace FileFormat.PfsArt;

/// <summary>Reads PFS: 1st Publisher clip art from bytes, streams, or file paths.</summary>
public static class PfsArtReader {

  public static PfsArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ART file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PfsArtFile FromStream(Stream stream) {
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

  public static PfsArtFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PfsArtFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid 1st Publisher ART file.");

    var width = data[2] | (data[3] << 8);
    var height = data[6] | (data[7] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid 1st Publisher ART dimensions: {width}x{height}.");

    // One bit a pixel, rows padded out to a whole 16-bit word.
    var stride = (width + 15) / 16 * 2;
    if (PfsArtFile.HeaderSize + (stride * height) > data.Length)
      throw new InvalidDataException("1st Publisher ART file is shorter than its dimensions require.");

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var row = PfsArtFile.HeaderSize + (y * stride);
      for (var x = 0; x < width; ++x) {
        var set = (data[row + (x >> 3)] >> (7 - (x & 7)) & 1) != 0;
        pixels[(y * width) + x] = set ? (byte)0 : (byte)255; // a set bit is ink
      }
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }

  public static PfsArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
