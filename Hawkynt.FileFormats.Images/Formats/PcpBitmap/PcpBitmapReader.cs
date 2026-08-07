using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PcpBitmap;

/// <summary>Reads .pcp bitmaps from bytes, streams, or file paths.</summary>
public static class PcpBitmapReader {

  public static PcpBitmapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcpBitmapFile FromStream(Stream stream) {
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

  public static PcpBitmapFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= PcpBitmapFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a .pcp bitmap: got {data.Length} bytes.");

    // Largest coordinates, not sizes.
    var width = BinaryPrimitives.ReadUInt16BigEndian(data) + 1;
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[2..]) + 1;

    var stride = MonochromePage.BytesPerRow(width);
    var needed = PcpBitmapFile.HeaderSize + stride * height;

    // Nothing states what the file is, so the size it claims has to account for it. A couple of
    // bytes may trail the bitmap; anything further off is a different format under the same name.
    if (data.Length < needed || data.Length > needed + 16)
      throw new InvalidDataException(
        $"A {width}x{height} .pcp bitmap is {needed} bytes, got {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      Trailer = BinaryPrimitives.ReadUInt16BigEndian(data[4..]),
      PixelData = data.Slice(PcpBitmapFile.HeaderSize, stride * height).ToArray(),
    };
  }

  public static PcpBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
