using System;
using System.IO;
using System.Text;

namespace FileFormat.XvThumbnail;

/// <summary>Reads XV thumbnail files from bytes, streams, or file paths.</summary>
public static class XvThumbnailReader {

  private static ReadOnlySpan<byte> Magic => "P7 332"u8;
  private const int _MaximumPixels = 100_000_000;

  public static XvThumbnailFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XV thumbnail file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XvThumbnailFile FromStream(Stream stream) {
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

  public static XvThumbnailFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Magic.Length + 1 || !data[..Magic.Length].SequenceEqual(Magic))
      throw new InvalidDataException("Invalid XV thumbnail magic: expected 'P7 332'.");

    var offset = Magic.Length;
    _ConsumeLineEnding(data, ref offset, "XV thumbnail magic");

    ReadOnlySpan<byte> dimensionLine;
    while (true) {
      if (offset >= data.Length)
        throw new InvalidDataException("No dimension line found in XV thumbnail header.");

      dimensionLine = _ReadLine(data, ref offset);
      if (dimensionLine.IsEmpty)
        continue;
      if (dimensionLine[0] == (byte)'#')
        continue;
      break;
    }

    var text = Encoding.ASCII.GetString(dimensionLine);
    var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 3 ||
        !int.TryParse(parts[0], out var width) ||
        !int.TryParse(parts[1], out var height) ||
        !int.TryParse(parts[2], out var maxValue))
      throw new InvalidDataException($"Expected 'width height 255' but got '{text}'.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("XV thumbnail dimensions must be positive.");
    if (maxValue != 255)
      throw new InvalidDataException($"XV thumbnail maxval must be 255, got {maxValue}.");

    var pixelCount = (long)width * height;
    if (pixelCount > _MaximumPixels)
      throw new InvalidDataException($"XV thumbnail exceeds the {_MaximumPixels:N0}-pixel implementation safety limit.");
    if (data.Length - offset < pixelCount)
      throw new InvalidDataException($"Truncated XV thumbnail raster: expected {pixelCount} bytes, got {data.Length - offset}.");

    return new XvThumbnailFile {
      Width = width,
      Height = height,
      PixelData = data.Slice(offset, (int)pixelCount).ToArray(),
    };
  }

  public static XvThumbnailFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static ReadOnlySpan<byte> _ReadLine(ReadOnlySpan<byte> data, ref int offset) {
    var start = offset;
    while (offset < data.Length && data[offset] is not ((byte)'\r') and not ((byte)'\n'))
      ++offset;

    var line = data[start..offset];
    if (offset < data.Length)
      _ConsumeLineEnding(data, ref offset, "XV thumbnail header line");
    return line;
  }

  private static void _ConsumeLineEnding(ReadOnlySpan<byte> data, ref int offset, string context) {
    if (offset >= data.Length)
      throw new InvalidDataException($"Missing line ending after {context}.");

    if (data[offset] == (byte)'\r') {
      ++offset;
      if (offset < data.Length && data[offset] == (byte)'\n')
        ++offset;
      return;
    }

    if (data[offset] == (byte)'\n') {
      ++offset;
      return;
    }

    throw new InvalidDataException($"Missing line ending after {context}.");
  }
}
