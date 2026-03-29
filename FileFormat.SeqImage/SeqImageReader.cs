using System;
using System.IO;

namespace FileFormat.SeqImage;

/// <summary>Reads SEQ image files from bytes, streams, or file paths.</summary>
public static class SeqImageReader {

  public static SeqImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SEQ file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SeqImageFile FromStream(Stream stream) {
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

  public static SeqImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SeqImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SEQ file (need at least {SeqImageFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != SeqImageFile.Magic[0] || data[1] != SeqImageFile.Magic[1] || data[2] != SeqImageFile.Magic[2] || data[3] != SeqImageFile.Magic[3])
      throw new InvalidDataException("Invalid SEQ magic bytes.");

    var version = BitConverter.ToUInt16(data, 4);
    var width = BitConverter.ToUInt16(data, 6);
    var height = BitConverter.ToUInt16(data, 8);
    var frameCount = BitConverter.ToUInt16(data, 10);
    var bpp = BitConverter.ToUInt16(data, 12);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid SEQ dimensions: {width}x{height}.");

    var pixelDataSize = data.Length - SeqImageFile.HeaderSize;
    var pixelData = new byte[pixelDataSize];
    if (pixelDataSize > 0)
      data.AsSpan(SeqImageFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Version = version,
      FrameCount = frameCount,
      Bpp = bpp,
      PixelData = pixelData,
    };
  }
}
