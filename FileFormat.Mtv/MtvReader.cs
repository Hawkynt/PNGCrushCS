using System;
using System.IO;
using System.Text;

namespace FileFormat.Mtv;

/// <summary>Reads MTV Ray Tracer files from bytes, streams, or file paths.</summary>
public static class MtvReader {

  public static MtvFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MTV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MtvFile FromStream(Stream stream) {
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

  public static MtvFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MtvFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    var newlineIndex = Array.IndexOf(data, (byte)'\n');
    if (newlineIndex < 0)
      throw new InvalidDataException("No newline found in MTV header.");

    var headerText = Encoding.ASCII.GetString(data, 0, newlineIndex);
    var parts = headerText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
      throw new InvalidDataException("Invalid MTV header dimensions.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("MTV image dimensions must be positive.");

    var pixelOffset = newlineIndex + 1;
    var expectedPixelBytes = width * height * 3;
    var available = data.Length - pixelOffset;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(pixelOffset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new MtvFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
