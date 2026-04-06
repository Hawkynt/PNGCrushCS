using System;
using System.IO;

namespace FileFormat.Vivid;

/// <summary>Reads Vivid Ray-Tracer image files from bytes, streams, or file paths.</summary>
public static class VividReader {

  public static VividFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Vivid file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VividFile FromStream(Stream stream) {
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

  public static VividFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static VividFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < VividFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Vivid file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 320;

    if (8 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 200;
    } else if (height <= 0 || height > 65535) {
      height = 200;
    }

    var pixelBytes = width * height * 3;
    var pixelData = new byte[pixelBytes];
    var available = Math.Min(pixelBytes, data.Length - VividFile.HeaderSize);
    if (available > 0)
      data.AsSpan(VividFile.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
