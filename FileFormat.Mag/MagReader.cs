using System;
using System.IO;

namespace FileFormat.Mag;

/// <summary>Reads MAKIchan Graphics files from bytes, streams, or file paths.</summary>
public static class MagReader {

  public static MagFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Mag file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MagFile FromStream(Stream stream) {
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

  public static MagFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MagFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Mag file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 640;

    if (32 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 400;
    } else if (height <= 0 || height > 65535) {
      height = 400;
    }

    var pixelBytes = width * height;
    var pixelData = new byte[pixelBytes];
    var available = Math.Min(pixelBytes, data.Length - MagFile.HeaderSize);
    if (available > 0)
      data.Slice(MagFile.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static MagFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MagFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Mag file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 640;

    if (32 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 400;
    } else if (height <= 0 || height > 65535) {
      height = 400;
    }

    var pixelBytes = width * height;
    var pixelData = new byte[pixelBytes];
    var available = Math.Min(pixelBytes, data.Length - MagFile.HeaderSize);
    if (available > 0)
      data.AsSpan(MagFile.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
