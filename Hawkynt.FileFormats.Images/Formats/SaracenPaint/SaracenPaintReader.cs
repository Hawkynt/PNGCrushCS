using System;
using System.IO;

namespace FileFormat.SaracenPaint;

/// <summary>Reads Saracen Paint pictures from bytes, streams, or file paths.</summary>
public static class SaracenPaintReader {

  public static SaracenPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SaracenPaintFile FromStream(Stream stream) {
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

  public static SaracenPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != SaracenPaintFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid Saracen Paint file size (expected {SaracenPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var bitmap = new byte[SaracenPaintFile.BitmapDataSize];
    data.Slice(SaracenPaintFile.BitmapOffset, SaracenPaintFile.BitmapDataSize).CopyTo(bitmap.AsSpan(0));

    var matrix = new byte[SaracenPaintFile.VideoMatrixSize];
    data.Slice(SaracenPaintFile.VideoMatrixOffset, SaracenPaintFile.VideoMatrixSize).CopyTo(matrix.AsSpan(0));

    var colors = new byte[SaracenPaintFile.ColorRamSize];
    data.Slice(SaracenPaintFile.ColorRamOffset, SaracenPaintFile.ColorRamSize).CopyTo(colors.AsSpan(0));

    return new() {
      LoadAddress = (ushort)(data[0] | (data[1] << 8)),
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = SaracenPaintFile.BackgroundOffset < 0 ? (byte)0 : data[SaracenPaintFile.BackgroundOffset],
    };
  }

  public static SaracenPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
