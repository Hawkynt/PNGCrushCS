using System;
using System.IO;

namespace FileFormat.DolphinEd;

/// <summary>Reads Dolphin Ed C64 multicolor files from bytes, streams, or file paths.</summary>
public static class DolphinEdReader {

  public static DolphinEdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dolphin Ed file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DolphinEdFile FromStream(Stream stream) {
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

  public static DolphinEdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DolphinEdFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Dolphin Ed file (expected {DolphinEdFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != DolphinEdFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Dolphin Ed file size (expected {DolphinEdFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += DolphinEdFile.LoadAddressSize;

    var bitmapData = new byte[DolphinEdFile.BitmapDataSize];
    data.AsSpan(offset, DolphinEdFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += DolphinEdFile.BitmapDataSize;

    var videoMatrix = new byte[DolphinEdFile.VideoMatrixSize];
    data.AsSpan(offset, DolphinEdFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += DolphinEdFile.VideoMatrixSize;

    var colorRam = new byte[DolphinEdFile.ColorRamSize];
    data.AsSpan(offset, DolphinEdFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += DolphinEdFile.ColorRamSize;

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
