using System;
using System.IO;

namespace FileFormat.CDUPaint;

/// <summary>Reads Commodore 64 CDU-Paint files from bytes, streams, or file paths.</summary>
public static class CDUPaintReader {

  public static CDUPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CDU-Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CDUPaintFile FromStream(Stream stream) {
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

  public static CDUPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CDUPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid CDU-Paint file (expected {CDUPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != CDUPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CDU-Paint file size (expected {CDUPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += CDUPaintFile.LoadAddressSize;

    var bitmapData = new byte[CDUPaintFile.BitmapDataSize];
    data.Slice(offset, CDUPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += CDUPaintFile.BitmapDataSize;

    var videoMatrix = new byte[CDUPaintFile.VideoMatrixSize];
    data.Slice(offset, CDUPaintFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += CDUPaintFile.VideoMatrixSize;

    var colorRam = new byte[CDUPaintFile.ColorRamSize];
    data.Slice(offset, CDUPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += CDUPaintFile.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
    }

  public static CDUPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CDUPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid CDU-Paint file (expected {CDUPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != CDUPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CDU-Paint file size (expected {CDUPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += CDUPaintFile.LoadAddressSize;

    var bitmapData = new byte[CDUPaintFile.BitmapDataSize];
    data.AsSpan(offset, CDUPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += CDUPaintFile.BitmapDataSize;

    var videoMatrix = new byte[CDUPaintFile.VideoMatrixSize];
    data.AsSpan(offset, CDUPaintFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += CDUPaintFile.VideoMatrixSize;

    var colorRam = new byte[CDUPaintFile.ColorRamSize];
    data.AsSpan(offset, CDUPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += CDUPaintFile.ColorRamSize;

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
