using System;
using System.IO;

namespace FileFormat.Farbfeld;

/// <summary>Reads Farbfeld files from bytes, streams, or file paths.</summary>
public static class FarbfeldReader {

  private static readonly byte[] _Magic = "farbfeld"u8.ToArray();

  public static FarbfeldFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Farbfeld file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FarbfeldFile FromStream(Stream stream) {
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

  public static FarbfeldFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < FarbfeldHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Farbfeld file.");

    var span = data;

    // Validate magic
    for (var i = 0; i < _Magic.Length; ++i)
      if (span[i] != _Magic[i])
        throw new InvalidDataException("Invalid Farbfeld signature.");

    var header = FarbfeldHeader.ReadFrom(span);
    var width = header.Width;
    var height = header.Height;

    var pixelDataLength = width * height * 8; // 4 channels x 2 bytes each
    var expectedFileSize = FarbfeldHeader.StructSize + pixelDataLength;
    if (data.Length < expectedFileSize)
      throw new InvalidDataException("Data too small for the declared image dimensions.");

    var pixelData = new byte[pixelDataLength];
    data.Slice(FarbfeldHeader.StructSize, pixelDataLength).CopyTo(pixelData.AsSpan(0));

    return new FarbfeldFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
    }

  public static FarbfeldFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FarbfeldHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Farbfeld file.");

    var span = data.AsSpan();

    // Validate magic
    for (var i = 0; i < _Magic.Length; ++i)
      if (span[i] != _Magic[i])
        throw new InvalidDataException("Invalid Farbfeld signature.");

    var header = FarbfeldHeader.ReadFrom(span);
    var width = header.Width;
    var height = header.Height;

    var pixelDataLength = width * height * 8; // 4 channels x 2 bytes each
    var expectedFileSize = FarbfeldHeader.StructSize + pixelDataLength;
    if (data.Length < expectedFileSize)
      throw new InvalidDataException("Data too small for the declared image dimensions.");

    var pixelData = new byte[pixelDataLength];
    data.AsSpan(FarbfeldHeader.StructSize, pixelDataLength).CopyTo(pixelData.AsSpan(0));

    return new FarbfeldFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };
  }
}
