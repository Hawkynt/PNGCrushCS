using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Wrappers;

namespace FileFormat.EmbeddedDib;

/// <summary>Reads packed Windows DIBs and, separately, searches enclosing files for one.</summary>
public static class EmbeddedDibReader {

  public static EmbeddedDibFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EmbeddedDibFile FromStream(Stream stream) {
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

  /// <summary>Decodes a packed DIB that begins at the first byte.</summary>
  public static EmbeddedDibFile FromSpan(ReadOnlySpan<byte> data)
    => new() {
      Preview = WrappedDib.Decode(data, 0, EmbeddedDibFile.MaxDimension, "The DIB"),
      Offset = 0,
    };

  /// <summary>Finds the first decodable packed DIB anywhere in a larger container.</summary>
  /// <remarks>
  /// Use this only for containers whose preview position is not known. A format that states its DIB
  /// offset should call <see cref="DecodeHeaderless"/> or <see cref="WrappedDib.Decode"/> directly;
  /// a stated location is stronger evidence than a byte pattern found by searching.
  /// </remarks>
  public static EmbeddedDibFile FindInSpan(ReadOnlySpan<byte> data) {
    for (var at = 0; at + EmbeddedDibFile.MinHeaderSize <= data.Length; ++at) {
      if (WrappedDib.Measure(data, at, EmbeddedDibFile.MaxDimension) < 0)
        continue;

      try {
        return new() {
          Preview = WrappedDib.Decode(data, at, EmbeddedDibFile.MaxDimension, "The embedded DIB"),
          Offset = at,
        };
      } catch (Exception) {
        // Heuristic search: a run that only looked like a bitmap header is not evidence enough.
      }
    }

    throw new InvalidDataException("No Windows bitmap preview was found in this file.");
  }

  /// <summary>Decodes a packed DIB at a location already established by its enclosing format.</summary>
  public static RawImage DecodeHeaderless(ReadOnlySpan<byte> data)
    => WrappedDib.Decode(data, 0, EmbeddedDibFile.MaxDimension, "The embedded DIB");

  public static EmbeddedDibFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
