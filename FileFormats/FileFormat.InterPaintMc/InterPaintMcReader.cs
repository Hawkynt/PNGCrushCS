using System;
using System.IO;

namespace FileFormat.InterPaintMc;

/// <summary>Reads Commodore 64 InterPaint Multicolor files from bytes, streams, or file paths.</summary>
public static class InterPaintMcReader {

  public static InterPaintMcFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("InterPaint Multicolor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterPaintMcFile FromStream(Stream stream) {
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

  public static InterPaintMcFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < InterPaintMcFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid InterPaint Multicolor file (expected {InterPaintMcFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != InterPaintMcFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid InterPaint Multicolor file size (expected {InterPaintMcFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += InterPaintMcFile.LoadAddressSize;

    var bitmapData = new byte[InterPaintMcFile.BitmapDataSize];
    data.Slice(offset, InterPaintMcFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += InterPaintMcFile.BitmapDataSize;

    var videoMatrix = new byte[InterPaintMcFile.VideoMatrixSize];
    data.Slice(offset, InterPaintMcFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += InterPaintMcFile.VideoMatrixSize;

    var colorRam = new byte[InterPaintMcFile.ColorRamSize];
    data.Slice(offset, InterPaintMcFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += InterPaintMcFile.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor
    };
    }

  public static InterPaintMcFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < InterPaintMcFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid InterPaint Multicolor file (expected {InterPaintMcFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != InterPaintMcFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid InterPaint Multicolor file size (expected {InterPaintMcFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += InterPaintMcFile.LoadAddressSize;

    var bitmapData = new byte[InterPaintMcFile.BitmapDataSize];
    data.AsSpan(offset, InterPaintMcFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += InterPaintMcFile.BitmapDataSize;

    var videoMatrix = new byte[InterPaintMcFile.VideoMatrixSize];
    data.AsSpan(offset, InterPaintMcFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += InterPaintMcFile.VideoMatrixSize;

    var colorRam = new byte[InterPaintMcFile.ColorRamSize];
    data.AsSpan(offset, InterPaintMcFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += InterPaintMcFile.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor
    };
  }
}
