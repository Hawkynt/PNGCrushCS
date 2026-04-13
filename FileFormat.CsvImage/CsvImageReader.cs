using System;
using System.IO;
using System.Text;

namespace FileFormat.CsvImage;

/// <summary>Reads CSV image files from bytes, streams, or file paths.</summary>
public static class CsvImageReader {

  public static CsvImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CSV image file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CsvImageFile FromStream(Stream stream) {
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

  public static CsvImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CsvImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid CSV image file (need at least {CsvImageFile.MinFileSize} bytes, got {data.Length}).");

    var text = Encoding.ASCII.GetString(data);
    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    if (lines.Length < 1)
      throw new InvalidDataException("CSV image file contains no lines.");

    var headerParts = lines[0].Split(',');
    if (headerParts.Length < 2 || !int.TryParse(headerParts[0].Trim(), out var width) || !int.TryParse(headerParts[1].Trim(), out var height))
      throw new InvalidDataException("Invalid CSV image header: expected 'width,height'.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid CSV image dimensions: {width}x{height}.");

    var pixelData = new byte[width * height];
    var pixelIndex = 0;

    for (var lineIdx = 1; lineIdx < lines.Length && pixelIndex < pixelData.Length; ++lineIdx) {
      var values = lines[lineIdx].Split(',', StringSplitOptions.RemoveEmptyEntries);
      for (var valIdx = 0; valIdx < values.Length && pixelIndex < pixelData.Length; ++valIdx) {
        if (byte.TryParse(values[valIdx].Trim(), out var value))
          pixelData[pixelIndex] = value;

        ++pixelIndex;
      }
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  
  }

  public static CsvImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
