using System;
using System.IO;

namespace FileFormat.Blazon;

/// <summary>Reads Blazon pictures from bytes, streams, or file paths.</summary>
public static class BlazonReader {

  public static BlazonFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BlazonFile FromStream(Stream stream) => FromSpan(StreamBytes.ReadAll(stream));

  public static BlazonFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != BlazonFile.ExpectedFileSize)
      throw new InvalidDataException($"A Blazon picture is {BlazonFile.ExpectedFileSize} bytes, got {data.Length}.");

    var bitmap = new byte[BlazonFile.BitmapDataSize];
    data.Slice(BlazonFile.BitmapOffset, BlazonFile.BitmapDataSize).CopyTo(bitmap);

    var matrix = new byte[BlazonFile.VideoMatrixSize];
    data.Slice(BlazonFile.VideoMatrixOffset, BlazonFile.VideoMatrixSize).CopyTo(matrix);

    var colors = new byte[BlazonFile.ColorRamSize];
    data.Slice(BlazonFile.ColorRamOffset, BlazonFile.ColorRamSize).CopyTo(colors);

    return new() {
      LoadAddress = (ushort)(data[0] | (data[1] << 8)),
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = data[BlazonFile.BackgroundOffset],
    };
  }

  public static BlazonFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
