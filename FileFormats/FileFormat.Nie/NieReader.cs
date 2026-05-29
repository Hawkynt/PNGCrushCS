using System;
using System.IO;

namespace FileFormat.Nie;

/// <summary>Reads NIE (Wuffs Naive Image) files from bytes, streams, or file paths.</summary>
public static class NieReader {

  public static NieFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NIE file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NieFile FromStream(Stream stream) {
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

  public static NieFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < NieFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid NIE file.");

    if (data[0] != 0x6E || data[1] != 0xC3 || data[2] != 0xAF || data[3] != 0x45)
      throw new InvalidDataException("Invalid NIE magic bytes.");

    var configByte = data[4];
    if (configByte is not ((byte)NiePixelConfig.Bgra8 or (byte)NiePixelConfig.BgraPremul8 or (byte)NiePixelConfig.Bgra16 or (byte)NiePixelConfig.BgraPremul16))
      throw new InvalidDataException($"Invalid NIE pixel config byte: 0x{configByte:X2}.");

    var pixelConfig = (NiePixelConfig)configByte;
    var header = NieHeader.ReadFrom(data);
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid NIE dimensions: {width}x{height}.");

    var bytesPerPixel = pixelConfig is NiePixelConfig.Bgra16 or NiePixelConfig.BgraPremul16 ? 8 : 4;
    var expectedDataSize = (long)width * height * bytesPerPixel;

    if (data.Length - NieFile.HeaderSize < expectedDataSize)
      throw new InvalidDataException("Data too small for the declared image dimensions.");

    var pixelData = new byte[(int)expectedDataSize];
    data.Slice(NieFile.HeaderSize, pixelData.Length).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      PixelConfig = pixelConfig,
      PixelData = pixelData,
    };
  
  }

  public static NieFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
