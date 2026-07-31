using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.TrzmielCompressed;

/// <summary>Reads compressed Trzmiel pictures from bytes, streams, or file paths.</summary>
public static class TrzmielCompressedReader {

  /// <summary>The screen is stored outright.</summary>
  private const byte _STORED = 0;

  /// <summary>The screen is packed column by column.</summary>
  private const byte _COLUMNS = 1;

  /// <summary>The screen is packed straight through.</summary>
  private const byte _LINEAR = 2;

  public static TrzmielCompressedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TrzmielCompressedFile FromStream(Stream stream) {
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

  public static TrzmielCompressedFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 2)
      throw new InvalidDataException($"Not a Trzmiel picture: {data.Length} bytes.");

    var screen = new byte[TrzmielCompressedFile.ScreenSize];

    switch (data[0]) {
      case _STORED:
        if (data.Length - 1 != TrzmielCompressedFile.ScreenSize)
          throw new InvalidDataException($"A stored Trzmiel picture is not {data.Length} bytes.");

        data[1..].CopyTo(screen);
        break;

      case _COLUMNS: {
        // Each of the eighty byte-columns is a run of its own, and a run may span the boundary
        // between them — which is why one decoder walks them all rather than one per column.
        var rle = new AtariKoalaRle(data, 1);
        for (var column = 0; column < TrzmielCompressedFile.Stride; ++column)
        for (var offset = column; offset < TrzmielCompressedFile.ColumnStride; offset += TrzmielCompressedFile.Stride)
          rle.Unpack(screen, offset, TrzmielCompressedFile.ColumnStride, TrzmielCompressedFile.ScreenSize);

        break;
      }

      case _LINEAR:
        new AtariKoalaRle(data, 1).Unpack(screen, 0, 1, TrzmielCompressedFile.ScreenSize);
        break;

      default:
        throw new InvalidDataException($"A Trzmiel picture is packed 0, 1 or 2, not {data[0]}.");
    }

    return new() { ScreenData = screen };
  }

  public static TrzmielCompressedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
