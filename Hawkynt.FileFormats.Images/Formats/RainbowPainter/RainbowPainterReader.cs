using System;
using System.IO;

namespace FileFormat.RainbowPainter;

/// <summary>Reads Rainbow Painter pictures from bytes, streams, or file paths.</summary>
public static class RainbowPainterReader {

  public static RainbowPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RainbowPainterFile FromStream(Stream stream) {
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

  public static RainbowPainterFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != RainbowPainterFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid Rainbow Painter file size (expected {RainbowPainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    var bitmap = new byte[RainbowPainterFile.BitmapDataSize];
    data.Slice(RainbowPainterFile.BitmapOffset, RainbowPainterFile.BitmapDataSize).CopyTo(bitmap.AsSpan(0));

    var matrix = new byte[RainbowPainterFile.VideoMatrixSize];
    data.Slice(RainbowPainterFile.VideoMatrixOffset, RainbowPainterFile.VideoMatrixSize).CopyTo(matrix.AsSpan(0));

    var colors = new byte[RainbowPainterFile.ColorRamSize];
    data.Slice(RainbowPainterFile.ColorRamOffset, RainbowPainterFile.ColorRamSize).CopyTo(colors.AsSpan(0));

    return new() {
      LoadAddress = (ushort)(data[0] | (data[1] << 8)),
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = RainbowPainterFile.BackgroundOffset < 0 ? (byte)0 : data[RainbowPainterFile.BackgroundOffset],
    };
  }

  public static RainbowPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
