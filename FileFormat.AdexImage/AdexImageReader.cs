using System;
using System.IO;

namespace FileFormat.AdexImage;

/// <summary>Reads ADEX image files from bytes, streams, or file paths.</summary>
public static class AdexImageReader {

  public static AdexImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ADX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AdexImageFile FromStream(Stream stream) {
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

  public static AdexImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AdexImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid ADX file (need at least {AdexImageFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != AdexImageFile.Magic[0] || data[1] != AdexImageFile.Magic[1] || data[2] != AdexImageFile.Magic[2] || data[3] != AdexImageFile.Magic[3])
      throw new InvalidDataException("Invalid ADX magic bytes.");

    var width = BitConverter.ToUInt16(data, 4);
    var height = BitConverter.ToUInt16(data, 6);
    var bpp = BitConverter.ToUInt16(data, 8);
    var compression = BitConverter.ToUInt16(data, 10);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid ADX dimensions: {width}x{height}.");

    var pixelDataSize = data.Length - AdexImageFile.HeaderSize;
    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.AsSpan(AdexImageFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Bpp = bpp,
      Compression = compression,
      PixelData = pixelData,
    };
  }
}
