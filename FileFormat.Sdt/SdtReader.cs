using System;
using System.IO;

namespace FileFormat.Sdt;

/// <summary>Reads SmartDraw thumbnail files from bytes, streams, or file paths.</summary>
public static class SdtReader {

  public static SdtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sdt file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SdtFile FromStream(Stream stream) {
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

  public static SdtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SdtFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Sdt file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 128;

    if (8 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 128;
    } else if (height <= 0 || height > 65535) {
      height = 128;
    }

    var pixelBytes = width * height * 3;
    var pixelData = new byte[pixelBytes];
    var available = Math.Min(pixelBytes, data.Length - SdtFile.HeaderSize);
    if (available > 0)
      data.AsSpan(SdtFile.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
