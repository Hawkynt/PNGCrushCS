using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.LaserData;

/// <summary>Reads LaserData document images from bytes, streams, or file paths.</summary>
public static class LaserDataReader {

  public static LaserDataFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LDA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LaserDataFile FromStream(Stream stream) {
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

  public static LaserDataFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static LaserDataFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < LaserDataFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid LDA file (need at least {LaserDataFile.MinFileSize} bytes, got {data.Length}).");

    if (BinaryPrimitives.ReadUInt16LittleEndian(data) != LaserDataFile.Magic)
      throw new InvalidDataException("Invalid LDA magic bytes.");

    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var compression = (LaserDataCompression)data[12];
    var mostSignificantBitFirst = data[13] != 0;
    var verticalResolution = BinaryPrimitives.ReadUInt16LittleEndian(data[16..]);
    var horizontalResolution = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid LDA dimensions: {width}x{height}.");

    var payload = data[LaserDataFile.HeaderSize..].ToArray();

    // The header's fill-order byte says which end of a byte the coded bits start at. Only the
    // most-significant-first case is natural for the decoders, so the other one is turned round first.
    if (!mostSignificantBitFirst)
      _ReverseBitsInPlace(payload);

    var pixelData = compression switch {
      LaserDataCompression.Group3 => CcittG3Decoder.Decode(_SkipLeadingEol(payload), width, height),
      LaserDataCompression.Group4 => CcittG4Decoder.Decode(payload, width, height),
      _ => _ReadUncompressed(payload, width, height),
    };

    return new() {
      Width = width,
      Height = height,
      Compression = compression,
      IsMostSignificantBitFirst = mostSignificantBitFirst,
      HorizontalResolution = horizontalResolution,
      VerticalResolution = verticalResolution,
      PixelData = pixelData,
    };
  }

  /// <summary>Reads packed scan lines, complementing them because a set bit is white on disk.</summary>
  private static byte[] _ReadUncompressed(byte[] payload, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var needed = bytesPerRow * height;
    if (payload.Length < needed)
      throw new InvalidDataException("LDA file truncated: not enough pixel data.");

    var result = new byte[needed];
    for (var i = 0; i < needed; ++i)
      result[i] = (byte)~payload[i];

    return result;
  }

  /// <summary>
  /// Moves the bit stream past the EOL that a Group 3 line begins with.
  /// </summary>
  /// <remarks>
  /// T.4 puts an EOL — eleven zero bits and a one — in front of every line, and XnView's reader
  /// insists on it: a stream built without them decodes to a blank page. The Group 3 decoder in this
  /// library instead decodes a line and then steps over the EOL that follows it, so the two agree as
  /// soon as the leading EOL is out of the way.
  /// </remarks>
  private static byte[] _SkipLeadingEol(byte[] payload) {
    var zeros = 0;
    for (var i = 0; i < payload.Length; ++i)
      for (var bit = 7; bit >= 0; --bit) {
        if (((payload[i] >> bit) & 1) == 0) {
          ++zeros;
          continue;
        }

        // A one arriving before eleven zeros means this stream does not start on an EOL.
        return zeros < CcittHuffmanTable.EolBitLength - 1 ? payload : _ShiftLeft(payload, zeros + 1);
      }

    return payload;
  }

  /// <summary>Returns the bit stream with its first <paramref name="bits"/> bits dropped.</summary>
  private static byte[] _ShiftLeft(byte[] payload, int bits) {
    var wholeBytes = bits >> 3;
    var remainder = bits & 7;
    var result = new byte[payload.Length - wholeBytes];

    for (var i = 0; i < result.Length; ++i) {
      var high = payload[wholeBytes + i] << remainder;
      var low = wholeBytes + i + 1 < payload.Length ? payload[wholeBytes + i + 1] >> (8 - remainder) : 0;
      result[i] = (byte)(remainder == 0 ? payload[wholeBytes + i] : high | low);
    }

    return result;
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
