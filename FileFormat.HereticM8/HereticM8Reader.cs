using System;
using System.IO;

namespace FileFormat.HereticM8;

/// <summary>Reads Heretic II MipMap texture files from bytes, streams, or file paths.</summary>
public static class HereticM8Reader {

  public static HereticM8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HereticM8 file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HereticM8File FromStream(Stream stream) {
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

  public static HereticM8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HereticM8File.HeaderSize)
      throw new InvalidDataException("Data too small for a valid HereticM8 file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 256;

    if (8 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 256;
    } else if (height <= 0 || height > 65535) {
      height = 256;
    }

    var pixelBytes = width * height;
    var pixelData = new byte[pixelBytes];
    var available = Math.Min(pixelBytes, data.Length - HereticM8File.HeaderSize);
    if (available > 0)
      data.AsSpan(HereticM8File.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
