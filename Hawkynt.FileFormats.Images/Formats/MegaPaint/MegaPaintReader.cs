using System;
using System.IO;

namespace FileFormat.MegaPaint;

/// <summary>Reads Atari ST MegaPaint monochrome images from bytes, streams, or file paths.</summary>
public static class MegaPaintReader {

  public static MegaPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MegaPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MegaPaintFile FromStream(Stream stream) {
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

  public static MegaPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MegaPaintFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid MegaPaint file: expected at least {MegaPaintFile.MinFileSize} bytes, got {data.Length}.");

    var header = MegaPaintHeader.ReadFrom(data);
    var width = header.Width;
    var height = header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid MegaPaint dimensions: {width}x{height}.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelDataSize = bytesPerRow * height;
    var availablePixelData = data.Length - MegaPaintFile.HeaderSize;

    // Three of the four samples are a second form of MegaPaint that opens with a signature word
    // rather than a size — 0xFF21 and 0xFDC9 turn up where the last column would be, which is no
    // width — and carries its screen packed rather than plain. Their second word is still the last
    // row, and RECOIL and XnView draw all three, so it is a real form and not damage. It is not
    // decoded here; saying so is better than reading a size out of a signature.
    if (availablePixelData < expectedPixelDataSize)
      throw new InvalidDataException(availablePixelData * 2 < expectedPixelDataSize
        ? $"This is a packed MegaPaint picture ({data.Length} bytes against the {expectedPixelDataSize + MegaPaintFile.HeaderSize} a plain {width}x{height} one takes), which is not decoded here."
        : $"Data too small for {width}x{height} MegaPaint image: expected {expectedPixelDataSize} bytes of pixel data, got {availablePixelData}.");

    var pixelData = new byte[expectedPixelDataSize];
    data.Slice(MegaPaintFile.HeaderSize, expectedPixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new MegaPaintFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static MegaPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
