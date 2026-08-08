using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.Synu;

/// <summary>Reads Synu pictures from bytes, streams, or file paths.</summary>
public static class SynuReader {

  public static SynuFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Synu picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SynuFile FromStream(Stream stream) {
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

  public static SynuFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SynuFile.Marker.Length
        || !Encoding.ASCII.GetString(data[..SynuFile.Marker.Length].ToArray()).Equals(SynuFile.Marker, StringComparison.Ordinal))
      throw new InvalidDataException("Not a Synu picture: it does not open with 'image '.");

    // Five lines: the name and byte count, then the width, the height, the channel count and what
    // the channels mean. The picture starts on the byte after the fifth newline.
    var at = 0;
    var lines = new string[5];
    for (var i = 0; i < lines.Length; ++i) {
      var end = data[at..].IndexOf((byte)'\n');
      if (end < 0)
        throw new InvalidDataException($"A Synu header is five lines; this one ends after {i}.");

      lines[i] = Encoding.ASCII.GetString(data.Slice(at, end).ToArray()).Trim();
      at += end + 1;
    }

    if (!int.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
        || !int.TryParse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
        || !int.TryParse(lines[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channels))
      throw new InvalidDataException("A Synu header states its width, height and channel count as decimal lines; these are not.");

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid Synu size: {width}x{height}.");
    if (channels is not (1 or 3))
      throw new InvalidDataException($"A Synu picture has one channel or three; this one states {channels}.");

    var stride = width * channels;
    if (data.Length - at < (long)stride * height)
      throw new InvalidDataException($"A {width}x{height} Synu picture needs {(long)stride * height} bytes of samples, got {data.Length - at}.");

    // Stored from the bottom up.
    var pixels = new byte[stride * height];
    for (var y = 0; y < height; ++y)
      data.Slice(at + (height - 1 - y) * stride, stride).CopyTo(pixels.AsSpan(y * stride));

    return new() {
      Width = width,
      Height = height,
      Channels = channels,
      ColorSpace = lines[4],
      PixelData = pixels,
    };
  }

  public static SynuFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
