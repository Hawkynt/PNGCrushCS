using System;
using System.IO;
using System.Text;

namespace FileFormat.MgrBitmap;

/// <summary>Reads MGR bitmap files from bytes, streams, or file paths.</summary>
public static class MgrBitmapReader {

  private const int _MIN_FILE_SIZE = 6;

  public static MgrBitmapFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MGR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MgrBitmapFile FromStream(Stream stream) {
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

  public static MgrBitmapFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MgrBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException($"Data too small for a valid MGR file: expected at least {_MIN_FILE_SIZE} bytes, got {data.Length}.");

    var newlineIdx = Array.IndexOf(data, (byte)'\n');
    if (newlineIdx < 0)
      throw new InvalidDataException("Invalid MGR header: missing newline terminator.");

    var headerLine = Encoding.ASCII.GetString(data, 0, newlineIdx);
    var xIdx = headerLine.IndexOf('x', StringComparison.OrdinalIgnoreCase);
    if (xIdx < 0)
      throw new InvalidDataException("Invalid MGR header: missing 'x' dimension separator.");

    if (!int.TryParse(headerLine[..xIdx].Trim(), out var width) || width <= 0)
      throw new InvalidDataException($"Invalid MGR width in header: '{headerLine[..xIdx].Trim()}'.");

    if (!int.TryParse(headerLine[(xIdx + 1)..].Trim(), out var height) || height <= 0)
      throw new InvalidDataException($"Invalid MGR height in header: '{headerLine[(xIdx + 1)..].Trim()}'.");

    var pixelOffset = newlineIdx + 1;
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;

    if (data.Length < pixelOffset + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {pixelOffset + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(pixelOffset, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new MgrBitmapFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }
}
