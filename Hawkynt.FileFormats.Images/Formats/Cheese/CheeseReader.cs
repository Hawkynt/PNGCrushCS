using System;
using System.IO;

namespace FileFormat.Cheese;

/// <summary>Reads Commodore 64 Cheese paint files from bytes, streams, or file paths.</summary>
public static class CheeseReader {

  public static CheeseFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cheese file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CheeseFile FromStream(Stream stream) {
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

  public static CheeseFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != CheeseFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid Cheese file size (expected {{CheeseFile.ExpectedFileSize}} bytes, got {{data.Length}}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmapData = new byte[CheeseFile.BitmapDataSize];
    data.Slice(CheeseFile.BitmapOffset, CheeseFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    var videoMatrix = new byte[CheeseFile.VideoMatrixSize];
    data.Slice(CheeseFile.VideoMatrixOffset, CheeseFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));

    var colorRam = new byte[CheeseFile.ColorRamSize];
    data.Slice(CheeseFile.ColorRamOffset, CheeseFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = data[CheeseFile.BackgroundOffset],
    };
  }

  public static CheeseFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
