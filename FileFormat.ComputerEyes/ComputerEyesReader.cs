using System;
using System.IO;

namespace FileFormat.ComputerEyes;

/// <summary>Reads ComputerEyes image files from bytes, streams, or file paths.</summary>
public static class ComputerEyesReader {

  public static ComputerEyesFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ComputerEyes file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ComputerEyesFile FromStream(Stream stream) {
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

  public static ComputerEyesFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ComputerEyesFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid ComputerEyes file: expected at least {ComputerEyesFile.HeaderSize} bytes, got {data.Length}.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);

    if (width <= 0)
      throw new InvalidDataException($"Invalid ComputerEyes width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid ComputerEyes height: {height}.");

    var expectedPixelBytes = width * height;
    if (data.Length < ComputerEyesFile.HeaderSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {ComputerEyesFile.HeaderSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(ComputerEyesFile.HeaderSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new ComputerEyesFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
