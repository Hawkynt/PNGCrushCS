using System;
using System.IO;

namespace FileFormat.Uhdr;

/// <summary>Reads UHDR files from bytes, streams, or file paths.</summary>
public static class UhdrReader {

  public static UhdrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("UHDR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UhdrFile FromStream(Stream stream) {
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

  public static UhdrFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static UhdrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < UhdrHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid UHDR file.");

    var header = UhdrHeader.ReadFrom(data.AsSpan());

    if (header.Magic != UhdrHeader.MagicValue)
      throw new InvalidDataException($"Invalid UHDR magic: expected '{UhdrHeader.MagicValue}', got '{header.Magic}'.");

    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("UHDR image dimensions must be non-zero.");

    var expectedPixelBytes = width * height * 3;
    var available = data.Length - UhdrHeader.StructSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(UhdrHeader.StructSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new UhdrFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
