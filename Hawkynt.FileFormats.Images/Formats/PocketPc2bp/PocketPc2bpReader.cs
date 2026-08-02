using System;
using System.IO;

namespace FileFormat.PocketPc2bp;

/// <summary>Reads Pocket PC 2bp bitmap files from bytes, streams, or file paths.</summary>
public static class PocketPc2bpReader {

  public static PocketPc2bpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PocketPc2bp file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PocketPc2bpFile FromStream(Stream stream) {
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

  public static PocketPc2bpFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PocketPc2bpFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid PocketPc2bp file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 64;

    if (8 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 64;
    } else if (height <= 0 || height > 65535) {
      height = 64;
    }

    var pixelBytes = (width + 7) / 8 * height;
    var pixelData = new byte[pixelBytes];
    // Padding what the file does not contain turns a misread size into a picture: a header taken
    // from the wrong offset asked for millions of pixels, the few hundred bytes present were
    // copied in, and the rest was zeros reported as a successful read.
    var available = data.Length - PocketPc2bpFile.HeaderSize;
    if (available < pixelBytes)
      throw new InvalidDataException($"Expected {pixelBytes} bytes of pixel data, got {available}.");

    data.Slice(PocketPc2bpFile.HeaderSize, pixelBytes).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static PocketPc2bpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
