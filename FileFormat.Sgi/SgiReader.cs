using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Sgi;

/// <summary>Reads SGI image files from bytes, streams, or file paths.</summary>
public static class SgiReader {

  public static SgiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SGI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SgiFile FromStream(Stream stream) {
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

  public static SgiFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SgiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SgiHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid SGI file.");

    var header = SgiHeader.ReadFrom(data.AsSpan());
    if (header.Magic != 0x01DA)
      throw new InvalidDataException("Invalid SGI magic number.");

    var width = header.XSize;
    var height = header.YSize;
    var channels = header.ZSize;
    var bytesPerChannel = header.BytesPerChannel;
    var compression = (SgiCompression)header.Compression;
    var colorMode = (SgiColorMode)header.Colormap;

    // Dimension 1 means single scanline (height=1, channels=1)
    if (header.Dimension == 1) {
      height = 1;
      channels = 1;
    } else if (header.Dimension == 2)
      channels = 1;

    var scanlineSize = width * bytesPerChannel;
    var totalPixelBytes = scanlineSize * height * channels;

    byte[] pixelData;
    if (compression == SgiCompression.Rle) {
      pixelData = _ReadRleData(data, width, height, channels, bytesPerChannel);
    } else {
      pixelData = new byte[totalPixelBytes];
      var srcOffset = SgiHeader.StructSize;
      var available = Math.Min(totalPixelBytes, data.Length - srcOffset);
      if (available > 0)
        data.AsSpan(srcOffset, available).CopyTo(pixelData.AsSpan(0));
    }

    return new SgiFile {
      Width = width,
      Height = height,
      Channels = channels,
      BytesPerChannel = bytesPerChannel,
      Compression = compression,
      ColorMode = colorMode,
      ImageName = header.ImageName,
      PixelData = pixelData
    };
  }

  private static byte[] _ReadRleData(byte[] data, int width, int height, int channels, int bytesPerChannel) {
    var tableEntries = height * channels;
    var scanlineSize = width * bytesPerChannel;
    var totalPixelBytes = scanlineSize * height * channels;
    var pixelData = new byte[totalPixelBytes];

    // Read offset and length tables (big-endian int32)
    var tableOffset = SgiHeader.StructSize;
    var offsets = new int[tableEntries];
    var lengths = new int[tableEntries];

    for (var i = 0; i < tableEntries; ++i)
      offsets[i] = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(tableOffset + i * 4));

    var lengthTableOffset = tableOffset + tableEntries * 4;
    for (var i = 0; i < tableEntries; ++i)
      lengths[i] = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(lengthTableOffset + i * 4));

    // Decompress each scanline per channel
    for (var channel = 0; channel < channels; ++channel)
      for (var row = 0; row < height; ++row) {
        var tableIdx = channel * height + row;
        var decompressed = SgiRleCompressor.Decompress(data, offsets[tableIdx], lengths[tableIdx], scanlineSize);
        var destOffset = (channel * height + row) * scanlineSize;
        decompressed.AsSpan(0, scanlineSize).CopyTo(pixelData.AsSpan(destOffset));
      }

    return pixelData;
  }
}
