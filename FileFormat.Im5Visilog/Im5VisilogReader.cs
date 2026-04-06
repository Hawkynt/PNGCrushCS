using System;
using System.IO;

namespace FileFormat.Im5Visilog;

/// <summary>Reads IM5 Visilog grayscale files from bytes, streams, or file paths.</summary>
public static class Im5VisilogReader {

  public static Im5VisilogFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IM5 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Im5VisilogFile FromStream(Stream stream) {
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

  public static Im5VisilogFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Im5VisilogFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Im5VisilogFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid IM5 file (need at least {Im5VisilogFile.MinFileSize} bytes, got {data.Length}).");

    var width = BitConverter.ToInt32(data, 0);
    var height = BitConverter.ToInt32(data, 4);
    var depth = BitConverter.ToInt32(data, 8);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid IM5 dimensions: {width}x{height}.");

    if (depth != 8 && depth != 16)
      throw new InvalidDataException($"Unsupported IM5 depth: {depth}.");

    var bytesPerPixel = depth / 8;
    var pixelDataSize = width * height * bytesPerPixel;
    if (data.Length < Im5VisilogFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("IM5 file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(Im5VisilogFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Depth = depth,
      PixelData = pixelData,
    };
  }
}
