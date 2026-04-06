using System;
using System.IO;

namespace FileFormat.RainbowPainter;

/// <summary>Reads Commodore 64 Rainbow Painter files from bytes, streams, or file paths.</summary>
public static class RainbowPainterReader {

  public static RainbowPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Rainbow Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RainbowPainterFile FromStream(Stream stream) {
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

  public static RainbowPainterFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static RainbowPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RainbowPainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Rainbow Painter file (expected {RainbowPainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != RainbowPainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Rainbow Painter file size (expected {RainbowPainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += RainbowPainterFile.LoadAddressSize;

    var bitmapData = new byte[RainbowPainterFile.BitmapDataSize];
    data.AsSpan(offset, RainbowPainterFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += RainbowPainterFile.BitmapDataSize;

    var videoMatrix = new byte[RainbowPainterFile.VideoMatrixSize];
    data.AsSpan(offset, RainbowPainterFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += RainbowPainterFile.VideoMatrixSize;

    var colorRam = new byte[RainbowPainterFile.ColorRamSize];
    data.AsSpan(offset, RainbowPainterFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += RainbowPainterFile.ColorRamSize;

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
