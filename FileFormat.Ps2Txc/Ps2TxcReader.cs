using System;
using System.IO;

namespace FileFormat.Ps2Txc;

/// <summary>Reads PS2 TXC texture files from bytes, streams, or file paths.</summary>
public static class Ps2TxcReader {

  public static Ps2TxcFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TXC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Ps2TxcFile FromStream(Stream stream) {
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

  public static Ps2TxcFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Ps2TxcFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Ps2TxcFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid TXC file (need at least {Ps2TxcFile.MinFileSize} bytes, got {data.Length}).");

    var width = BitConverter.ToUInt16(data, 0);
    var height = BitConverter.ToUInt16(data, 2);
    var bpp = BitConverter.ToUInt16(data, 4);
    var flags = BitConverter.ToUInt16(data, 6);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid TXC dimensions: {width}x{height}.");

    if (bpp != 16 && bpp != 24 && bpp != 32)
      throw new InvalidDataException($"Unsupported TXC bits per pixel: {bpp}.");

    var pixelDataSize = width * height * (bpp / 8);
    if (data.Length < Ps2TxcFile.HeaderSize + pixelDataSize)
      throw new InvalidDataException("TXC file truncated: not enough pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(Ps2TxcFile.HeaderSize, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      Flags = flags,
      PixelData = pixelData,
    };
  }
}
