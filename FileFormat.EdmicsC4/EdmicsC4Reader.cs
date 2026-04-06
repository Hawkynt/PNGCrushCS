using System;
using System.IO;

namespace FileFormat.EdmicsC4;

/// <summary>Reads EDMICS C4 fax image files from bytes, streams, or file paths.</summary>
public static class EdmicsC4Reader {

  public static EdmicsC4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EdmicsC4 file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EdmicsC4File FromStream(Stream stream) {
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

  public static EdmicsC4File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static EdmicsC4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < EdmicsC4File.HeaderSize)
      throw new InvalidDataException("Data too small for a valid EdmicsC4 file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 1728;

    if (16 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 2200;
    } else if (height <= 0 || height > 65535) {
      height = 2200;
    }

    var pixelBytes = (width + 7) / 8 * height;
    var pixelData = new byte[pixelBytes];
    var available = Math.Min(pixelBytes, data.Length - EdmicsC4File.HeaderSize);
    if (available > 0)
      data.AsSpan(EdmicsC4File.HeaderSize, available).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
