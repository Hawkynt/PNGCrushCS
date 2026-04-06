using System;
using System.IO;

namespace FileFormat.EggPaint;

/// <summary>Reads Commodore 64 Egg Paint files from bytes, streams, or file paths.</summary>
public static class EggPaintReader {

  public static EggPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Egg Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EggPaintFile FromStream(Stream stream) {
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

  public static EggPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static EggPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < EggPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Egg Paint file (expected {EggPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != EggPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Egg Paint file size (expected {EggPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += EggPaintFile.LoadAddressSize;

    var bitmapData = new byte[EggPaintFile.BitmapDataSize];
    data.AsSpan(offset, EggPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += EggPaintFile.BitmapDataSize;

    var videoMatrix = new byte[EggPaintFile.VideoMatrixSize];
    data.AsSpan(offset, EggPaintFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += EggPaintFile.VideoMatrixSize;

    var colorRam = new byte[EggPaintFile.ColorRamSize];
    data.AsSpan(offset, EggPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += EggPaintFile.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
  }
}
