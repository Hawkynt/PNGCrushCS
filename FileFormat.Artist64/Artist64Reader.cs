using System;
using System.IO;

namespace FileFormat.Artist64;

/// <summary>Reads Commodore 64 Artist 64 files from bytes, streams, or file paths.</summary>
public static class Artist64Reader {

  public static Artist64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Artist 64 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Artist64File FromStream(Stream stream) {
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

  public static Artist64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Artist64File.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Artist 64 file (expected {Artist64File.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != Artist64File.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Artist 64 file size (expected {Artist64File.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += Artist64File.LoadAddressSize;

    var bitmapData = new byte[Artist64File.BitmapDataSize];
    data.AsSpan(offset, Artist64File.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += Artist64File.BitmapDataSize;

    var videoMatrix = new byte[Artist64File.VideoMatrixSize];
    data.AsSpan(offset, Artist64File.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += Artist64File.VideoMatrixSize;

    var colorRam = new byte[Artist64File.ColorRamSize];
    data.AsSpan(offset, Artist64File.ColorRamSize).CopyTo(colorRam.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
    };
  }
}
