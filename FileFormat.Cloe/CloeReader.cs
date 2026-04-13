using System;
using System.IO;

namespace FileFormat.Cloe;

/// <summary>Reads Cloe Ray-Tracer image files from bytes, streams, or file paths.</summary>
public static class CloeReader {

  public static CloeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cloe file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CloeFile FromStream(Stream stream) {
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

  public static CloeFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CloeFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Cloe file.");

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
    var available = Math.Min(pixelBytes, data.Length - CloeFile.HeaderSize);
    if (available > 0)
      data.Slice(CloeFile.HeaderSize, available).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  public static CloeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
