using System;
using System.IO;

namespace FileFormat.DigiView;

/// <summary>Reads DigiView digitizer files from bytes, streams, or file paths.</summary>
public static class DigiViewReader {

  public static DigiViewFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DigiView file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DigiViewFile FromStream(Stream stream) {
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

  public static DigiViewFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < DigiViewFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid DigiView file (minimum {DigiViewFile.HeaderSize} bytes, got {data.Length}).");

    // Width (2 bytes, big-endian)
    var width = (ushort)((data[0] << 8) | data[1]);

    // Height (2 bytes, big-endian)
    var height = (ushort)((data[2] << 8) | data[3]);

    // Channels (1 byte)
    var channels = data[4];

    if (width == 0)
      throw new InvalidDataException("Invalid DigiView width: 0.");
    if (height == 0)
      throw new InvalidDataException("Invalid DigiView height: 0.");
    if (channels != 1 && channels != 3)
      throw new InvalidDataException($"Invalid DigiView channel count: {channels}. Expected 1 (grayscale) or 3 (RGB).");

    var pixelDataSize = width * height * channels;
    var expectedSize = DigiViewFile.HeaderSize + pixelDataSize;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Data too small for pixel data: expected {expectedSize} bytes, got {data.Length}.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(DigiViewFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      Channels = channels,
      PixelData = pixelData,
    };
  }
}
