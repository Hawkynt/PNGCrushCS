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

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmapData = new byte[CDUPaintFile.BitmapDataSize];
    data.Slice(CDUPaintFile.BitmapOffset, CDUPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    var videoMatrix = new byte[CDUPaintFile.VideoMatrixSize];
    data.Slice(CDUPaintFile.VideoMatrixOffset, CDUPaintFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));

    var colorRam = new byte[CDUPaintFile.ColorRamSize];
    data.Slice(CDUPaintFile.ColorRamOffset, CDUPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));

    var backgroundColor = data[CDUPaintFile.BackgroundOffset];

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
    return FromSpan(data);
  }
}
