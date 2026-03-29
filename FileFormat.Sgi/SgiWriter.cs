using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Sgi;

/// <summary>Assembles SGI image file bytes from pixel data.</summary>
public static class SgiWriter {

  public static byte[] ToBytes(SgiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var channels = file.Channels;
    var bytesPerChannel = file.BytesPerChannel;
    var scanlineSize = width * bytesPerChannel;

    ushort dimension = channels > 1 ? (ushort)3 : height > 1 ? (ushort)2 : (ushort)1;

    var header = new SgiHeader(
      0x01DA,
      (byte)file.Compression,
      (byte)bytesPerChannel,
      dimension,
      (ushort)width,
      (ushort)height,
      (ushort)channels,
      0,
      bytesPerChannel == 1 ? 255 : 65535,
      0,
      file.ImageName ?? string.Empty,
      (int)file.ColorMode
    );

    if (file.Compression == SgiCompression.Rle)
      return _WriteRle(header, file.PixelData, width, height, channels, bytesPerChannel);

    return _WriteUncompressed(header, file.PixelData);
  }

  private static byte[] _WriteUncompressed(SgiHeader header, byte[] pixelData) {
    var result = new byte[SgiHeader.StructSize + pixelData.Length];
    header.WriteTo(result.AsSpan());
    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(SgiHeader.StructSize));
    return result;
  }

  private static byte[] _WriteRle(SgiHeader header, byte[] pixelData, int width, int height, int channels, int bytesPerChannel) {
    var tableEntries = height * channels;
    var scanlineSize = width * bytesPerChannel;

    // Compress all scanlines first
    var compressedScanlines = new byte[tableEntries][];
    for (var channel = 0; channel < channels; ++channel)
      for (var row = 0; row < height; ++row) {
        var tableIdx = channel * height + row;
        var srcOffset = tableIdx * scanlineSize;
        var scanline = new byte[scanlineSize];
        var available = Math.Min(scanlineSize, pixelData.Length - srcOffset);
        if (available > 0)
          pixelData.AsSpan(srcOffset, available).CopyTo(scanline.AsSpan(0));

        compressedScanlines[tableIdx] = SgiRleCompressor.Compress(scanline);
      }

    // Calculate data region start (after header + offset table + length table)
    var tablesSize = tableEntries * 4 * 2;
    var dataStart = SgiHeader.StructSize + tablesSize;

    // Build offset and length tables
    var offsets = new int[tableEntries];
    var lengths = new int[tableEntries];
    var currentOffset = dataStart;
    for (var i = 0; i < tableEntries; ++i) {
      offsets[i] = currentOffset;
      lengths[i] = compressedScanlines[i].Length;
      currentOffset += lengths[i];
    }

    // Assemble output
    var totalSize = currentOffset;
    var result = new byte[totalSize];
    header.WriteTo(result.AsSpan());

    // Write offset table
    var tableOffset = SgiHeader.StructSize;
    for (var i = 0; i < tableEntries; ++i)
      BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(tableOffset + i * 4), offsets[i]);

    // Write length table
    var lengthTableOffset = tableOffset + tableEntries * 4;
    for (var i = 0; i < tableEntries; ++i)
      BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(lengthTableOffset + i * 4), lengths[i]);

    // Write compressed data
    for (var i = 0; i < tableEntries; ++i)
      compressedScanlines[i].AsSpan(0, lengths[i]).CopyTo(result.AsSpan(offsets[i]));

    return result;
  }
}
