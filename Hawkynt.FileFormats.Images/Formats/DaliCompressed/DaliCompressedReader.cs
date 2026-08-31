using System;
using System.IO;

namespace FileFormat.DaliCompressed;

/// <summary>Reads compressed Atari ST Dali screens from bytes, streams, or file paths.</summary>
public static class DaliCompressedReader {

  public static DaliCompressedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dali screen not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName), DaliCompressedFile.ResolutionFromExtension(file.Extension));
  }

  public static DaliCompressedFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  /// <summary>Reads bytes as the primary .LPK variant because resolution is not stored in the stream.</summary>
  public static DaliCompressedFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, DaliResolution.Low);

  public static DaliCompressedFile FromSpan(ReadOnlySpan<byte> data, DaliResolution resolution) {
    _ = DaliCompressedFile.Geometry(resolution);
    if (data.Length <= DaliCompressedFile.LengthsOffset)
      throw new InvalidDataException("Data too small for a compressed Dali screen.");

    var offset = DaliCompressedFile.LengthsOffset;
    var countLength = _ParseLength(data, ref offset);
    var valueLength = _ParseLength(data, ref offset);

    if (countLength is < 1 or > DaliCompressor.GroupCount)
      throw new InvalidDataException($"Compressed Dali count table must contain 1..{DaliCompressor.GroupCount} bytes.");
    if (valueLength != checked(countLength * DaliCompressor.GroupSize))
      throw new InvalidDataException("Compressed Dali value table must contain exactly one four-byte value per count entry.");

    var expectedLength = checked(offset + countLength + valueLength);
    if (expectedLength != data.Length)
      throw new InvalidDataException($"Compressed Dali declares {expectedLength} total bytes but the file contains {data.Length}.");

    var palette = data[..DaliCompressedFile.PaletteSize].ToArray();
    var screen = DaliCompressor.Decompress(
      data.Slice(offset, countLength),
      data.Slice(offset + countLength, valueLength));

    return new() { Resolution = resolution, Palette = palette, ScreenData = screen };
  }

  public static DaliCompressedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static DaliCompressedFile FromBytes(byte[] data, DaliResolution resolution) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, resolution);
  }

  /// <summary>Parses one ASCII decimal length terminated by CR LF, advancing past it.</summary>
  private static int _ParseLength(ReadOnlySpan<byte> data, ref int offset) {
    var value = 0;
    var digits = 0;
    while (offset < data.Length && data[offset] is >= (byte)'0' and <= (byte)'9') {
      value = checked(value * 10 + (data[offset++] - '0'));
      if (++digits > 6)
        throw new InvalidDataException("Compressed Dali length field is implausibly long.");
    }

    if (digits == 0 || offset + 1 >= data.Length || data[offset] != '\r' || data[offset + 1] != '\n')
      throw new InvalidDataException("Compressed Dali length field is not ASCII decimal followed by CR LF.");

    offset += 2;
    return value;
  }

  /// <summary>Compatibility helper retained for callers that already use the reader's mapping.</summary>
  internal static DaliResolution ResolutionFromExtension(string extension)
    => DaliCompressedFile.ResolutionFromExtension(extension);
}
