using System;
using System.IO;

namespace FileFormat.PaintMagic;

/// <summary>Reads Paint Magic pictures from bytes, streams, or file paths.</summary>
public static class PaintMagicReader {

  public static PaintMagicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PaintMagicFile FromStream(Stream stream) {
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

  public static PaintMagicFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != PaintMagicFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid Paint Magic file size (expected {PaintMagicFile.ExpectedFileSize} bytes, got {data.Length}).");

    var bitmap = new byte[PaintMagicFile.BitmapDataSize];
    data.Slice(PaintMagicFile.BitmapOffset, PaintMagicFile.BitmapDataSize).CopyTo(bitmap.AsSpan(0));

    var matrix = new byte[PaintMagicFile.VideoMatrixSize];
    data.Slice(PaintMagicFile.VideoMatrixOffset, PaintMagicFile.VideoMatrixSize).CopyTo(matrix.AsSpan(0));

    return new() {
      BitmapData = bitmap,
      VideoMatrix = matrix,
      BackgroundColor = data[PaintMagicFile.BackgroundOffset],
      SharedColor = data[PaintMagicFile.SharedColorOffset],
    };
  }

  public static PaintMagicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
