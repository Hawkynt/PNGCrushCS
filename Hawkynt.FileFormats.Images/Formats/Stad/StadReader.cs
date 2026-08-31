using System;
using System.IO;

namespace FileFormat.Stad;

/// <summary>Reads STAD compressed Atari ST screen images from bytes, streams, or file paths.</summary>
public static class StadReader {

  private const int _HeaderSize = 7;

  public static StadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("STAD file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static StadFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[checked((int)(stream.Length - stream.Position))];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static StadFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HeaderSize)
      throw new InvalidDataException($"A STAD header is {_HeaderSize} bytes; this file is {data.Length}.");

    var packing = data[..4] switch {
      [(byte)'p', (byte)'M', (byte)'8', (byte)'5'] => StadPacking.Horizontal,
      [(byte)'p', (byte)'M', (byte)'8', (byte)'6'] => StadPacking.Vertical,
      _ => throw new InvalidDataException("Invalid STAD signature; expected pM85 or pM86."),
    };

    var idByte = data[4];
    var packByte = data[5];
    var specialByte = data[6];
    if (idByte == specialByte)
      throw new InvalidDataException("STAD id and special escape bytes must differ.");

    var packedOrder = _Decompress(data[_HeaderSize..], idByte, packByte, specialByte);
    var screen = packing == StadPacking.Horizontal ? packedOrder : _TransposeFromColumns(packedOrder);

    return new StadFile {
      RawData = screen,
      Packing = packing,
      HasCompressionParameters = true,
      IdByte = idByte,
      PackByte = packByte,
      SpecialByte = specialByte,
    };
  }

  public static StadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <remarks>
  /// Both count bytes are interpreted as count-minus-one. That behavior is pinned by the repository's
  /// real STAD samples against RECOIL and XnView; some historical prose lists disagree for the second
  /// escape, but using a raw count there does not reproduce those files.
  /// </remarks>
  private static byte[] _Decompress(ReadOnlySpan<byte> encoded, byte idByte, byte packByte, byte specialByte) {
    var screen = new byte[StadFile.ScreenDataSize];
    var source = 0;
    var target = 0;

    while (target < screen.Length) {
      if (source >= encoded.Length)
        throw new InvalidDataException($"STAD stream expands to only {target} of {StadFile.ScreenDataSize} bytes.");

      var control = encoded[source++];
      if (control == idByte) {
        if (source >= encoded.Length)
          throw new InvalidDataException("Truncated STAD pack-byte run.");
        var count = encoded[source++] + 1;
        if (target + count > screen.Length)
          throw new InvalidDataException("STAD pack-byte run exceeds the screen bitmap.");
        screen.AsSpan(target, count).Fill(packByte);
        target += count;
        continue;
      }

      if (control == specialByte) {
        if (source + 1 >= encoded.Length)
          throw new InvalidDataException("Truncated STAD arbitrary-byte run.");
        var value = encoded[source++];
        var count = encoded[source++] + 1;
        if (target + count > screen.Length)
          throw new InvalidDataException("STAD arbitrary-byte run exceeds the screen bitmap.");
        screen.AsSpan(target, count).Fill(value);
        target += count;
        continue;
      }

      screen[target++] = control;
    }

    if (source != encoded.Length)
      throw new InvalidDataException("Unexpected trailing data after the complete STAD screen bitmap.");

    return screen;
  }

  private static byte[] _TransposeFromColumns(ReadOnlySpan<byte> columns) {
    var rows = new byte[columns.Length];
    for (var column = 0; column < StadFile.BytesPerRow; ++column)
      for (var row = 0; row < StadFile.PixelHeight; ++row)
        rows[row * StadFile.BytesPerRow + column] = columns[column * StadFile.PixelHeight + row];
    return rows;
  }
}
