using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Psd;

/// <summary>Reads PSD files from bytes, streams, or file paths.</summary>
public static class PsdReader {

  public static PsdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PSD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PsdFile FromStream(Stream stream) {
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

  public static PsdFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PsdHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid PSD file.");

    // Header (26 bytes)
    var header = PsdHeader.ReadFrom(data);
    if (header.Sig0 != (byte)'8' || header.Sig1 != (byte)'B' || header.Sig2 != (byte)'P' || header.Sig3 != (byte)'S')
      throw new InvalidDataException("Invalid PSD signature.");

    var channels = header.Channels;
    var height = header.Height;
    var width = header.Width;
    var depth = header.Depth;
    var colorMode = (PsdColorMode)header.ColorMode;

    var offset = PsdHeader.StructSize;

    // Color Mode Data section
    byte[]? palette = null;
    if (offset + 4 > data.Length)
      throw new InvalidDataException("Unexpected end of file in color mode data section.");

    var colorModeDataLength = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
    offset += 4;

    if (colorModeDataLength > 0) {
      if (offset + colorModeDataLength > data.Length)
        throw new InvalidDataException("Color mode data extends beyond file.");

      if (colorMode == PsdColorMode.Indexed && colorModeDataLength >= 768)
        palette = data.Slice(offset, 768).ToArray();

      offset += colorModeDataLength;
    }

    // Image Resources section
    byte[]? imageResources = null;
    if (offset + 4 > data.Length)
      throw new InvalidDataException("Unexpected end of file in image resources section.");

    var imageResourcesLength = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
    offset += 4;

    if (imageResourcesLength > 0) {
      if (offset + imageResourcesLength > data.Length)
        throw new InvalidDataException("Image resources data extends beyond file.");

      imageResources = data.Slice(offset, imageResourcesLength).ToArray();
      offset += imageResourcesLength;
    }

    // Layer and Mask Info section
    byte[]? layerMaskInfo = null;
    if (offset + 4 > data.Length)
      throw new InvalidDataException("Unexpected end of file in layer/mask info section.");

    var layerMaskInfoLength = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
    offset += 4;

    if (layerMaskInfoLength > 0) {
      if (offset + layerMaskInfoLength > data.Length)
        throw new InvalidDataException("Layer/mask info data extends beyond file.");

      layerMaskInfo = data.Slice(offset, layerMaskInfoLength).ToArray();
      offset += layerMaskInfoLength;
    }

    // Image Data section
    if (offset + 2 > data.Length)
      throw new InvalidDataException("Unexpected end of file in image data section.");

    var compression = (PsdCompression)BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
    offset += 2;

    var bytesPerChannel = (depth + 7) / 8;
    var scanlineLength = width * bytesPerChannel;
    var totalPixelDataLength = scanlineLength * height * channels;

    byte[] pixelData;
    if (compression == PsdCompression.Rle) {
      // Read per-scanline byte counts (height * channels entries, each 2 bytes big-endian)
      var scanlineCount = height * channels;
      var byteCountTableSize = scanlineCount * 2;
      if (offset + byteCountTableSize > data.Length)
        throw new InvalidDataException("RLE byte count table extends beyond file.");

      var byteCounts = new int[scanlineCount];
      for (var i = 0; i < scanlineCount; ++i)
        byteCounts[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + i * 2)..]);

      offset += byteCountTableSize;

      // Decompress each scanline using PackBits
      pixelData = new byte[totalPixelDataLength];
      var pixelOffset = 0;
      for (var i = 0; i < scanlineCount; ++i) {
        var compressedLength = byteCounts[i];
        if (offset + compressedLength > data.Length)
          throw new InvalidDataException("RLE compressed data extends beyond file.");

        _DecompressPackBits(data.Slice(offset, compressedLength), pixelData, ref pixelOffset, scanlineLength);
        offset += compressedLength;
      }
    } else {
      // Raw: channel-planar data
      var remaining = data.Length - offset;
      var copyLength = Math.Min(remaining, totalPixelDataLength);
      pixelData = new byte[totalPixelDataLength];
      data.Slice(offset, copyLength).CopyTo(pixelData.AsSpan(0));
    }

    return new PsdFile {
      Width = width,
      Height = height,
      Channels = channels,
      Depth = depth,
      ColorMode = colorMode,
      PixelData = pixelData,
      Palette = palette,
      ImageResources = imageResources,
      LayerMaskInfo = layerMaskInfo
    };
  }

  public static PsdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static void _DecompressPackBits(ReadOnlySpan<byte> source, byte[] destination, ref int destOffset, int expectedLength) {
    var srcIdx = 0;
    var written = 0;

    while (srcIdx < source.Length && written < expectedLength) {
      var header = (sbyte)source[srcIdx++];

      if (header >= 0) {
        // Literal: copy (header + 1) bytes
        var count = header + 1;
        for (var j = 0; j < count && srcIdx < source.Length && written < expectedLength; ++j) {
          destination[destOffset++] = source[srcIdx++];
          ++written;
        }
      } else if (header != -128) {
        // Run: repeat next byte (-header + 1) times
        var count = -header + 1;
        if (srcIdx >= source.Length)
          continue;

        var value = source[srcIdx++];
        for (var j = 0; j < count && written < expectedLength; ++j) {
          destination[destOffset++] = value;
          ++written;
        }
      }
      // header == -128 (0x80): no-op
    }
  }
}
