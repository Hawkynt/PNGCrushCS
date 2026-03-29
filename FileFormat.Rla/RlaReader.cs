using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Rla;

/// <summary>Reads RLA (Wavefront Run-Length) files from bytes, streams, or file paths.</summary>
public static class RlaReader {

  public static RlaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RLA file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RlaFile FromStream(Stream stream) {
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

  public static RlaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RlaHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid RLA file.");

    var header = RlaHeader.ReadFrom(data.AsSpan());

    var width = header.ActiveWindowRight - header.ActiveWindowLeft + 1;
    var height = header.ActiveWindowTop - header.ActiveWindowBottom + 1;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid RLA dimensions: {width}x{height}.");

    var totalChannels = header.NumChannels + header.NumMatte;

    // Read per-scanline offset table (int32 BE * height, stored right after header)
    var offsetTableStart = RlaHeader.StructSize;
    if (data.Length < offsetTableStart + height * 4)
      throw new InvalidDataException("Data too small for scanline offset table.");

    var offsets = new int[height];
    for (var i = 0; i < height; ++i)
      offsets[i] = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offsetTableStart + i * 4));

    // Decode scanlines (RLA stores bottom-to-top)
    var bytesPerChannel = header.NumBits <= 8 ? 1 : 2;
    var scanlineChannelSize = width * bytesPerChannel;
    var pixelData = new byte[width * height * totalChannels * bytesPerChannel];

    for (var row = 0; row < height; ++row) {
      var scanlineOffset = offsets[row];
      if (scanlineOffset < 0 || scanlineOffset >= data.Length)
        continue;

      var pos = scanlineOffset;
      for (var ch = 0; ch < totalChannels; ++ch) {
        if (pos + 2 > data.Length)
          break;

        var chunkLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
        pos += 2;

        if (pos + chunkLength > data.Length)
          break;

        var decompressed = RlaRleCompressor.Decompress(data.AsSpan(pos, chunkLength), scanlineChannelSize);
        var destOffset = (row * totalChannels + ch) * scanlineChannelSize;
        decompressed.AsSpan(0, Math.Min(decompressed.Length, scanlineChannelSize)).CopyTo(pixelData.AsSpan(destOffset));
        pos += chunkLength;
      }
    }

    return new RlaFile {
      Width = width,
      Height = height,
      NumChannels = header.NumChannels,
      NumMatte = header.NumMatte,
      NumBits = header.NumBits,
      StorageType = header.StorageType,
      FrameNumber = header.FrameNumber,
      Description = header.Description,
      ProgramName = header.ProgramName,
      PixelData = pixelData
    };
  }
}
