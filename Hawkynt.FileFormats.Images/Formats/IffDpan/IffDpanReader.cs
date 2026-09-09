using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Ilbm;

namespace FileFormat.IffDpan;

/// <summary>Reads Deluxe Paint DPAN animations and exposes their first ILBM frame.</summary>
public static class IffDpanReader {

  public static IffDpanFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DPAN file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffDpanFile FromStream(Stream stream) {
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

  public static IffDpanFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IffDpanFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IffDpanFile.MinFileSize)
      throw new InvalidDataException($"Invalid DPAN data: expected at least {IffDpanFile.MinFileSize} bytes, got {data.Length}.");
    if (!data[..4].SequenceEqual("FORM"u8))
      throw new InvalidDataException("Invalid DPAN data: expected a FORM container.");
    if (!data.Slice(8, 4).SequenceEqual("ANIM"u8))
      throw new InvalidDataException("Invalid DPAN data: expected FORM ANIM.");

    var formSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
    if (formSize < 4 || formSize > int.MaxValue || 8L + formSize > data.Length)
      throw new InvalidDataException("Invalid DPAN data: FORM ANIM size exceeds the available data.");

    var formEnd = checked(8 + (int)formSize);
    var offset = 12;
    while (offset < formEnd) {
      var chunk = _ReadChunk(data, offset, formEnd);

      if (data.Slice(offset, 4).SequenceEqual("FORM"u8)) {
        if (chunk.DataSize < 4)
          throw new InvalidDataException("Invalid DPAN data: nested FORM has no form type.");

        if (data.Slice(offset + 8, 4).SequenceEqual("ILBM"u8))
          return _ReadFirstFrame(data, offset, chunk.DataSize);
      }

      offset = chunk.NextOffset;
    }

    throw new InvalidDataException("Invalid DPAN data: FORM ANIM contains no ILBM frame.");
  }

  private static IffDpanFile _ReadFirstFrame(ReadOnlySpan<byte> data, int frameOffset, int frameDataSize) {
    var frameLength = checked(8 + frameDataSize);
    var frameEnd = checked(frameOffset + frameLength);
    var offset = frameOffset + 12;
    ushort version = 0;
    ushort frameCount = 0;
    uint flags = 0;
    var foundDpan = false;

    while (offset < frameEnd) {
      var chunk = _ReadChunk(data, offset, frameEnd);
      if (data.Slice(offset, 4).SequenceEqual("DPAN"u8)) {
        if (chunk.DataSize != 8)
          throw new InvalidDataException($"Invalid DPAN chunk size: expected 8 bytes, got {chunk.DataSize}.");

        var payload = data.Slice(offset + 8, 8);
        version = BinaryPrimitives.ReadUInt16BigEndian(payload);
        frameCount = BinaryPrimitives.ReadUInt16BigEndian(payload[2..]);
        flags = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
        foundDpan = true;
        break;
      }

      offset = chunk.NextOffset;
    }

    if (!foundDpan)
      throw new InvalidDataException("Invalid DPAN data: the first ILBM frame has no DPAN chunk.");

    var frame = IlbmReader.FromSpan(data.Slice(frameOffset, frameLength));
    var rawImage = IlbmFile.ToRawImage(frame);
    var pixels = rawImage.Format == PixelFormat.Rgb24
      ? rawImage.PixelData[..]
      : rawImage.ToRgb24();

    return new() {
      Width = frame.Width,
      Height = frame.Height,
      PixelData = pixels,
      RawData = data.ToArray(),
      Version = version,
      FrameCount = frameCount,
      Flags = flags,
    };
  }

  private static (int DataSize, int NextOffset) _ReadChunk(ReadOnlySpan<byte> data, int offset, int containingEnd) {
    if (offset < 0 || offset + 8 > containingEnd || containingEnd > data.Length)
      throw new InvalidDataException("Invalid DPAN data: truncated IFF chunk header.");

    var size = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 4, 4));
    if (size > int.MaxValue)
      throw new InvalidDataException("Invalid DPAN data: IFF chunk is too large.");

    var dataSize = (int)size;
    var paddedSize = checked(dataSize + (dataSize & 1));
    var nextOffset = checked(offset + 8 + paddedSize);
    if (nextOffset > containingEnd)
      throw new InvalidDataException("Invalid DPAN data: truncated IFF chunk payload.");

    return (dataSize, nextOffset);
  }
}
