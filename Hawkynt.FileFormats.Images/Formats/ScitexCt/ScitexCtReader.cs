using System;
using System.IO;
using System.Text;

namespace FileFormat.ScitexCt;

/// <summary>Reads Scitex CT files from bytes, streams, or file paths.</summary>
public static class ScitexCtReader {

  public static ScitexCtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Scitex CT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ScitexCtFile FromStream(Stream stream) {
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

  public static ScitexCtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ScitexCtFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ScitexCtHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Scitex CT file.");

    if (!ScitexCtHeader.IsContinuousTone(data))
      throw new InvalidDataException("Not a Scitex continuous-tone file: no CT at offset 80.");

    var (width, height, mode) = ScitexCtHeader.Read(data);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A Scitex CT states no size: {width}x{height}.");

    var channels = mode switch {
      ScitexCtColorMode.Grayscale => 1,
      ScitexCtColorMode.Rgb => 3,
      _ => 4,
    };

    var expected = width * height * channels;
    if (data.Length < ScitexCtHeader.StructSize + expected)
      throw new InvalidDataException(
        $"{width}x{height} in {channels} separations needs {ScitexCtHeader.StructSize + expected} bytes; this file is {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      BitsPerComponent = 8,
      ColorMode = mode,
      HResolution = 300,
      VResolution = 300,
      Description = string.Empty,
      PixelData = _SeparationsToChunky(data.Slice(ScitexCtHeader.StructSize, expected), width, height, channels),
    };
  }

  /// <summary>
  /// Turns the separations into pixels.
  /// </summary>
  /// <remarks>
  /// A Scitex CT holds each row one separation at a time — a whole row of the first, then a whole row
  /// of the second, and so on — rather than a channel at a time per pixel. This was copied straight
  /// into the picture, so the first three samples of the red separation became the red, green and
  /// blue of one pixel and the picture came out in bands of the wrong colour at a third of its width.
  /// <para/>
  /// Established against XnView and ImageMagick, which agree with each other: read this way the only
  /// sample matches both exactly, where before the mean error was 30 of 255 a channel.
  /// </remarks>
  private static byte[] _SeparationsToChunky(ReadOnlySpan<byte> separations, int width, int height, int channels) {
    if (channels == 1)
      return separations.ToArray();

    var chunky = new byte[separations.Length];

    for (var row = 0; row < height; ++row) {
      var rowStart = row * width * channels;
      for (var channel = 0; channel < channels; ++channel) {
        var from = rowStart + channel * width;
        for (var column = 0; column < width; ++column)
          chunky[rowStart + column * channels + channel] = separations[from + column];
      }
    }

    return chunky;
  }
}
