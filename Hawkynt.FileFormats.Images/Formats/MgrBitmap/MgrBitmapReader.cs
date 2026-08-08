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
    if (data.Length < MgrBitmapFile.ShortHeaderSize)
      throw new InvalidDataException(
        $"Data too small for a valid MGR file: expected at least {MgrBitmapFile.ShortHeaderSize} bytes, got {data.Length}.");

    // Both letters, not one. The one real sample opens 'zz' and was refused for not being 'yz',
    // which is the older of the two forms rather than the only one.
    if (data[1] != (byte)'z' || (data[0] != (byte)'y' && data[0] != (byte)'z'))
      throw new InvalidDataException("Not an MGR bitmap: it does not open with 'yz' or 'zz'.");

    // Six bits to a byte, biased into printable range so the whole header stays typable — which is
    // what an MGR header is for. This was read as the text "800x600" followed by a newline, which
    // is not a form the format has, so every real file was refused for want of an 'x'.
    var width = _Pair(data, 2);
    var height = _Pair(data, 4);

    if (width <= 0)
      throw new InvalidDataException($"Invalid MGR width in header: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid MGR height in header: {height}.");

    // Which of the two header lengths this is, decided by which one the file's length agrees with
    // rather than by the letters — the sample opens 'zz' and carries the shorter one. The longer
    // form states a depth in the seventh byte; the shorter one is a bit a pixel and states nothing.
    var stride = (width + 7) / 8;
    var header = MgrBitmapFile.ShortHeaderSize + stride * height == data.Length
      ? MgrBitmapFile.ShortHeaderSize
      : MgrBitmapFile.HeaderSize;

    if (header == MgrBitmapFile.HeaderSize) {
      if (data.Length < MgrBitmapFile.HeaderSize)
        throw new InvalidDataException($"Data too small for pixel data: expected {MgrBitmapFile.HeaderSize + stride * height} bytes, got {data.Length}.");

      var depth = data[6] - MgrBitmapFile.HeaderBias;
      if (depth != 1)
        throw new InvalidDataException($"Unsupported MGR depth: {depth}. Only one bit a pixel is read here.");
    }

    var needed = header + stride * height;
    if (data.Length < needed)
      throw new InvalidDataException($"Data too small for pixel data: expected {needed} bytes, got {data.Length}.");

    var pixelData = new byte[stride * height];
    data.Slice(header, pixelData.Length).CopyTo(pixelData);

    return new MgrBitmapFile {
      Width = width,
      Height = height,
      HasDepthByte = header == MgrBitmapFile.HeaderSize,
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
