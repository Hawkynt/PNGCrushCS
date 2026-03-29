using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Psb;

/// <summary>Reads PSB (Photoshop Big) files from bytes, streams, or file paths.</summary>
public static class PsbReader {

  public static PsbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PSB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PsbFile FromStream(Stream stream) {
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

  public static PsbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PsbHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid PSB file.");

    var span = data.AsSpan();

    // Header (26 bytes)
    var header = PsbHeader.ReadFrom(span);
    if (header.Sig0 != (byte)'8' || header.Sig1 != (byte)'B' || header.Sig2 != (byte)'P' || header.Sig3 != (byte)'S')
      throw new InvalidDataException("Invalid PSB signature.");

    if (header.Version != 2)
      throw new InvalidDataException($"Invalid PSB version {header.Version}; expected 2.");

    var channels = header.Channels;
    var height = header.Height;
    var width = header.Width;
    var depth = header.Depth;
    var colorMode = (PsbColorMode)header.ColorMode;

    var offset = PsbHeader.StructSize;

    // Color Mode Data section (4-byte length)
    byte[]? palette = null;
    if (offset + 4 > data.Length)
      throw new InvalidDataException("Unexpected end of file in color mode data section.");

    var colorModeDataLength = BinaryPrimitives.ReadInt32BigEndian(span[offset..]);
    offset += 4;

    if (colorModeDataLength > 0) {
      if (offset + colorModeDataLength > data.Length)
        throw new InvalidDataException("Color mode data extends beyond file.");

      if (colorMode == PsbColorMode.Indexed && colorModeDataLength >= 768)
        palette = data[offset..(offset + 768)];

      offset += colorModeDataLength;
    }

    // Image Resources section (4-byte length)
    byte[]? imageResources = null;
    if (offset + 4 > data.Length)
      throw new InvalidDataException("Unexpected end of file in image resources section.");

    var imageResourcesLength = BinaryPrimitives.ReadInt32BigEndian(span[offset..]);
    offset += 4;

    if (imageResourcesLength > 0) {
      if (offset + imageResourcesLength > data.Length)
        throw new InvalidDataException("Image resources data extends beyond file.");

      imageResources = data[offset..(offset + imageResourcesLength)];
      offset += imageResourcesLength;
    }

    // Layer and Mask Info section (PSB uses 8-byte length!)
    byte[]? layerMaskInfo = null;
    if (offset + 8 > data.Length)
      throw new InvalidDataException("Unexpected end of file in layer/mask info section.");

    var layerMaskInfoLength = BinaryPrimitives.ReadInt64BigEndian(span[offset..]);
    offset += 8;

    if (layerMaskInfoLength > 0) {
      if (offset + layerMaskInfoLength > data.Length)
        throw new InvalidDataException("Layer/mask info data extends beyond file.");

      layerMaskInfo = data[offset..(offset + (int)layerMaskInfoLength)];
      offset += (int)layerMaskInfoLength;
    }

    // Image Data section
    if (offset + 2 > data.Length)
      throw new InvalidDataException("Unexpected end of file in image data section.");

    var compression = (PsbCompression)BinaryPrimitives.ReadInt16BigEndian(span[offset..]);
    offset += 2;

    var bytesPerChannel = (depth + 7) / 8;
    var scanlineLength = width * bytesPerChannel;
    var totalPixelDataLength = scanlineLength * height * channels;

    byte[] pixelData;
    if (compression == PsbCompression.Rle) {
      // PSB uses 4-byte (int32 BE) byte counts per scanline
      var scanlineCount = height * channels;
      var byteCountTableSize = scanlineCount * 4;
      if (offset + byteCountTableSize > data.Length)
        throw new InvalidDataException("RLE byte count table extends beyond file.");

      var byteCounts = new int[scanlineCount];
      for (var i = 0; i < scanlineCount; ++i)
        byteCounts[i] = BinaryPrimitives.ReadInt32BigEndian(span[(offset + i * 4)..]);

      offset += byteCountTableSize;

      // Decompress each scanline using PackBits
      pixelData = new byte[totalPixelDataLength];
      var pixelOffset = 0;
      for (var i = 0; i < scanlineCount; ++i) {
        var compressedLength = byteCounts[i];
        if (offset + compressedLength > data.Length)
          throw new InvalidDataException("RLE compressed data extends beyond file.");

        _DecompressPackBits(span.Slice(offset, compressedLength), pixelData, ref pixelOffset, scanlineLength);
        offset += compressedLength;
      }
    } else {
      // Raw: channel-planar data
      var remaining = data.Length - offset;
      var copyLength = Math.Min(remaining, totalPixelDataLength);
      pixelData = new byte[totalPixelDataLength];
      data.AsSpan(offset, copyLength).CopyTo(pixelData.AsSpan(0));
    }

    return new PsbFile {
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
