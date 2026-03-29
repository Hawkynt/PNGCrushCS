using System;
using System.IO;

namespace FileFormat.PlotMaker;

/// <summary>Reads Plot Maker files from bytes, streams, or file paths.</summary>
public static class PlotMakerReader {

  public static PlotMakerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Plot Maker file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PlotMakerFile FromStream(Stream stream) {
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

  public static PlotMakerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PlotMakerFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid Plot Maker file (minimum {PlotMakerFile.HeaderSize} bytes, got {data.Length}).");

    var width = (ushort)(data[0] | (data[1] << 8));
    var height = (ushort)(data[2] | (data[3] << 8));

    if (width == 0)
      throw new InvalidDataException("Invalid Plot Maker width: 0.");
    if (height == 0)
      throw new InvalidDataException("Invalid Plot Maker height: 0.");

    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var expectedSize = PlotMakerFile.HeaderSize + expectedPixelBytes;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"Data too small for pixel data: expected {expectedSize} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(PlotMakerFile.HeaderSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
