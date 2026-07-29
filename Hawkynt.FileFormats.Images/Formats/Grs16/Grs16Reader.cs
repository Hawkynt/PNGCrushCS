using System;
using System.IO;

namespace FileFormat.Grs16;

/// <summary>Reads headerless raw 16-bit grayscale files from bytes, streams, or file paths.</summary>
public static class Grs16Reader {

  public static Grs16File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("G16 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Grs16File FromStream(Stream stream) {
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

  public static Grs16File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Grs16File.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid G16 file (need at least {Grs16File.MinFileSize} bytes, got {data.Length}).");

    var totalPixels = data.Length / 2;
    var width = Grs16File.DefaultWidth;
    var height = totalPixels / width;

    if (height == 0) {
      width = totalPixels;
      height = 1;
    }

    var usedBytes = width * height * 2;
    var pixelData = new byte[usedBytes];
    data.Slice(0, Math.Min(usedBytes, data.Length)).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static Grs16File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Grs16File.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid G16 file (need at least {Grs16File.MinFileSize} bytes, got {data.Length}).");

    var totalPixels = data.Length / 2;
    var width = Grs16File.DefaultWidth;
    var height = totalPixels / width;

    if (height == 0) {
      width = totalPixels;
      height = 1;
    }

    var usedBytes = width * height * 2;
    var pixelData = new byte[usedBytes];
    data.AsSpan(0, Math.Min(usedBytes, data.Length)).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
