using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.ImnetImage;

/// <summary>Reads IMNET document images from bytes, streams, or file paths.</summary>
public static class ImnetImageReader {

  public static ImnetImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IMT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImnetImageFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static ImnetImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ImnetImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ImnetImageFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid IMT file (need at least {ImnetImageFile.MinFileSize} bytes, got {data.Length}).");

    if (BinaryPrimitives.ReadUInt32BigEndian(data) != ImnetImageFile.Magic)
      throw new InvalidDataException("Invalid IMT magic bytes.");

    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
    var bytesPerRow = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
    var resolution = BinaryPrimitives.ReadUInt16LittleEndian(data[16..]);
    var mostSignificantBitFirst = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]) == 0;

    if (bytesPerRow <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid IMT dimensions: {bytesPerRow} bytes per line, {height} lines.");

    var width = bytesPerRow * 8;
    var payload = data[ImnetImageFile.HeaderSize..].ToArray();

    // The fill-order field says which end of a byte the coded bits start at; only the
    // most-significant-first case is natural for the decoder, so the other one is turned round first.
    if (!mostSignificantBitFirst)
      _ReverseBitsInPlace(payload);

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      IsMostSignificantBitFirst = mostSignificantBitFirst,
      PixelData = CcittG4Decoder.Decode(payload, width, height),
    };
  }

  private static void _ReverseBitsInPlace(byte[] payload) {
    for (var i = 0; i < payload.Length; ++i) {
      var value = payload[i];
      value = (byte)(((value & 0xF0) >> 4) | ((value & 0x0F) << 4));
      value = (byte)(((value & 0xCC) >> 2) | ((value & 0x33) << 2));
      payload[i] = (byte)(((value & 0xAA) >> 1) | ((value & 0x55) << 1));
    }
  }

}
