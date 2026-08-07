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

  public static MgrBitmapFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MgrBitmapFile.HeaderSize)
      throw new InvalidDataException(
        $"Data too small for a valid MGR file: expected at least {MgrBitmapFile.HeaderSize} bytes, got {data.Length}.");

    if (data[0] != (byte)'y' || data[1] != (byte)'z')
      throw new InvalidDataException("Not an MGR bitmap: it does not open with 'yz'.");

    // Six bits to a byte, biased into printable range so the whole header stays typable — which is
    // what an MGR header is for. This was read as the text "800x600" followed by a newline, which
    // is not a form the format has, so every real file was refused for want of an 'x'.
    var width = _Pair(data, 2);
    var height = _Pair(data, 4);
    var depth = data[6] - MgrBitmapFile.HeaderBias;

    if (width <= 0)
      throw new InvalidDataException($"Invalid MGR width in header: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid MGR height in header: {height}.");
    if (depth != 1)
      throw new InvalidDataException($"Unsupported MGR depth: {depth}. Only one bit a pixel is read here.");

    var stride = (width + 7) / 8;
    var needed = MgrBitmapFile.HeaderSize + stride * height;
    if (data.Length < needed)
      throw new InvalidDataException($"Data too small for pixel data: expected {needed} bytes, got {data.Length}.");

    var pixelData = new byte[stride * height];
    data.Slice(MgrBitmapFile.HeaderSize, pixelData.Length).CopyTo(pixelData);

    return new MgrBitmapFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  private static int _Pair(ReadOnlySpan<byte> data, int at)
    => ((data[at] - MgrBitmapFile.HeaderBias) << 6) | (data[at + 1] - MgrBitmapFile.HeaderBias);

  public static MgrBitmapFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
