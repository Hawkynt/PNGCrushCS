using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.InterleafImage;

/// <summary>Reads Interleaf images from bytes, streams, or file paths.</summary>
public static class InterleafImageReader {

  public static InterleafImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interleaf image not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterleafImageFile FromStream(Stream stream) {
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

  public static InterleafImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < InterleafImageFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an Interleaf image (got {data.Length} bytes).");

    if (!data[..InterleafImageFile.Magic.Length].SequenceEqual(InterleafImageFile.Magic))
      throw new InvalidDataException("Not an Interleaf image: it does not open the way one does.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(data[InterleafImageFile.WidthAt..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[InterleafImageFile.HeightAt..]);
    var bitsPerPixel = BinaryPrimitives.ReadUInt16BigEndian(data[InterleafImageFile.BitsPerPixelAt..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid Interleaf image size: {width}x{height}.");

    if (bitsPerPixel != InterleafImageFile.SupportedBitsPerPixel)
      throw new InvalidDataException($"An Interleaf image of {bitsPerPixel} bits is not one this reads; only {InterleafImageFile.SupportedBitsPerPixel} is.");

    // The header's size times its depth, plus the header, is the length of the file. That is what
    // says the size is being read where the format keeps it rather than somewhere that happens to
    // hold a plausible pair of numbers.
    var expected = InterleafImageFile.HeaderSize + (long)width * height * InterleafImageFile.PlaneCount;
    if (expected != data.Length)
      throw new InvalidDataException(
        $"An Interleaf image of {width}x{height} at {bitsPerPixel} bits accounts for {expected} bytes and the file is {data.Length}.");

    // A row of red, then that row's green, then its blue, and only then the next row.
    var pixels = new byte[width * height * InterleafImageFile.PlaneCount];
    var body = data[InterleafImageFile.HeaderSize..];
    for (var y = 0; y < height; ++y) {
      var line = y * width * InterleafImageFile.PlaneCount;
      var red = body[line..];
      var green = body[(line + width)..];
      var blue = body[(line + width * 2)..];

      for (int x = 0, at = line; x < width; ++x, at += InterleafImageFile.PlaneCount) {
        pixels[at] = red[x];
        pixels[at + 1] = green[x];
        pixels[at + 2] = blue[x];
      }
    }

    return new() {
      Width = width,
      Height = height,
      HorizontalResolution = BinaryPrimitives.ReadUInt16BigEndian(data[InterleafImageFile.HorizontalResolutionAt..]),
      VerticalResolution = BinaryPrimitives.ReadUInt16BigEndian(data[InterleafImageFile.VerticalResolutionAt..]),
      PixelData = pixels,
    };
  }

  public static InterleafImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
