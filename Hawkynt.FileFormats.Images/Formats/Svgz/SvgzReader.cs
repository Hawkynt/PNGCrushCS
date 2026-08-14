using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Svg;

namespace FileFormat.Svgz;

/// <summary>Takes the gzip off a drawing and reads what is underneath.</summary>
public static class SvgzReader {

  /// <summary>A ceiling on what one drawing may unpack to.</summary>
  /// <remarks>
  /// Gzip will happily expand a few kilobytes into gigabytes, and a picture is something a viewer
  /// opens without being asked twice, so the unpacking stops rather than filling memory on the word
  /// of the file it is reading.
  /// </remarks>
  private const int _MaximumUnpackedSize = 256 << 20;

  /// <summary>How much of a header a detector is willing to unpack before deciding.</summary>
  private const int _PeekSize = 4096;

  public static SvgzFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Gzipped SVG drawing not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SvgzFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static SvgzFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SvgzFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 3 || data[0] != SvgzFile.Magic0 || data[1] != SvgzFile.Magic1)
      throw new InvalidDataException("A gzipped SVG drawing starts with gzip's two bytes and this does not.");

    byte[] markup;
    try {
      markup = _Unpack(data, _MaximumUnpackedSize);
    } catch (InvalidDataException failure) {
      throw new InvalidDataException($"The gzip around this drawing does not unpack: {failure.Message}", failure);
    }

    return new() { Drawing = SvgReader.FromSpan(markup) };
  }

  /// <summary>
  /// As much of a truncated header as gzip will give up, for a detector deciding what it is holding.
  /// </summary>
  /// <remarks>
  /// The stream running out is the normal case here rather than an error — a detector is given the
  /// first few dozen bytes of a file — so what has already been decompressed is kept and the
  /// failure is not passed on.
  /// </remarks>
  internal static string PeekText(ReadOnlySpan<byte> header) {
    try {
      return Encoding.UTF8.GetString(_Unpack(header, _PeekSize));
    } catch (Exception) {
      return string.Empty;
    }
  }

  /// <summary>Inflates up to <paramref name="limit"/> bytes, keeping whatever arrived first.</summary>
  private static byte[] _Unpack(ReadOnlySpan<byte> data, int limit) {
    using var source = new MemoryStream(data.ToArray(), writable: false);
    using var gzip = new GZipStream(source, CompressionMode.Decompress);
    using var unpacked = new MemoryStream();

    var buffer = new byte[16 << 10];
    try {
      while (unpacked.Length < limit) {
        var read = gzip.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - unpacked.Length));
        if (read <= 0)
          break;

        unpacked.Write(buffer, 0, read);
      }
    } catch (Exception) when (unpacked.Length > 0 && limit == _PeekSize) {
      // A header stops mid-stream by definition; what came out before it did is the answer.
    }

    return unpacked.ToArray();
  }
}
