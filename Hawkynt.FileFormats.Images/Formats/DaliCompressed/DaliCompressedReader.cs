using System;
using System.IO;

namespace FileFormat.DaliCompressed;

/// <summary>Reads compressed Atari ST Dali screens from bytes, streams, or file paths.</summary>
public static class DaliCompressedReader {

  public static DaliCompressedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dali screen not found.", file.FullName);

    // Only the extension says which resolution the screen is in.
    return FromSpan(File.ReadAllBytes(file.FullName), ResolutionFromExtension(file.Extension));
  }

  public static DaliCompressedFile FromStream(Stream stream) {
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

  public static DaliCompressedFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, DaliResolution.Low);

  public static DaliCompressedFile FromSpan(ReadOnlySpan<byte> data, DaliResolution resolution) {
    if (data.Length <= DaliCompressedFile.LengthsOffset)
      throw new InvalidDataException("Data too small for a compressed Dali screen.");

    var offset = DaliCompressedFile.LengthsOffset;
    var countLength = _ParseLength(data, ref offset);
    var valueLength = _ParseLength(data, ref offset);
    if (countLength <= 0 || valueLength <= 0)
      throw new InvalidDataException("Compressed Dali screen declares an empty stream.");

    if (offset + countLength + valueLength > data.Length)
      throw new InvalidDataException("Compressed Dali streams run past the end of the file.");

    var palette = new byte[DaliCompressedFile.PaletteSize];
    data[..DaliCompressedFile.PaletteSize].CopyTo(palette);

    var screen = DaliCompressor.Decompress(
      data.Slice(offset, countLength),
      data.Slice(offset + countLength, valueLength));

    return new() { Resolution = resolution, Palette = palette, ScreenData = screen };
  }

  public static DaliCompressedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Parses one ASCII decimal length terminated by CR LF, advancing past it.</summary>
  private static int _ParseLength(ReadOnlySpan<byte> data, ref int offset) {
    var value = 0;
    var digits = 0;
    while (offset < data.Length && data[offset] >= (byte)'0' && data[offset] <= (byte)'9') {
      value = value * 10 + (data[offset++] - '0');
      if (++digits > 6)
        throw new InvalidDataException("Compressed Dali length field is implausibly long.");
    }

    if (digits == 0 || offset + 1 >= data.Length || data[offset] != '\r' || data[offset + 1] != '\n')
      throw new InvalidDataException("Compressed Dali length field is not ASCII decimal followed by CR LF.");

    offset += 2;
    return value;
  }

  /// <summary>The resolution an extension names; the writer needs the same answer the reader gives.</summary>
  internal static DaliResolution ResolutionFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".mpk" => DaliResolution.Medium,
    ".hpk" => DaliResolution.High,
    _ => DaliResolution.Low,
  };
}
