using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Flif.Codec;

namespace FileFormat.Flif;

/// <summary>Reads FLIF files from bytes, streams, or file paths.</summary>
public static class FlifReader {

  /// <summary>The "FLIF" magic bytes.</summary>
  private static readonly byte[] _Magic = [(byte)'F', (byte)'L', (byte)'I', (byte)'F'];

  /// <summary>Minimum valid file size: 4 (magic) + 1 (header byte) + 1 (bpc) + 1 (width varint) + 1 (height varint) + some compressed data.</summary>
  internal const int MinFileSize = 8;

  public static FlifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FlifFile FromStream(Stream stream) {
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

  public static FlifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MinFileSize)
      throw new InvalidDataException("Data too small for a valid FLIF file.");

    var span = data.AsSpan();

    // Validate magic "FLIF"
    for (var i = 0; i < _Magic.Length; ++i)
      if (span[i] != _Magic[i])
        throw new InvalidDataException("Invalid FLIF signature.");

    // Parse header byte (offset 4)
    var headerByte = span[4];
    var isInterlaced = (headerByte & 0x10) != 0;
    var isAnimated = (headerByte & 0x08) != 0;
    var channelBits = headerByte & 0x07;

    var channelCount = channelBits switch {
      1 => FlifChannelCount.Gray,
      3 => FlifChannelCount.Rgb,
      4 => FlifChannelCount.Rgba,
      _ => throw new InvalidDataException($"Invalid FLIF channel count: {channelBits}.")
    };

    // Parse bytes per channel (offset 5)
    var bpcByte = span[5];
    var bitsPerChannel = bpcByte switch {
      0 => 8,  // '1' => 8-bit
      1 => 16, // '2' => 16-bit
      _ => throw new InvalidDataException($"Unsupported FLIF bytes-per-channel value: {bpcByte}.")
    };

    // Parse varint-encoded dimensions
    var offset = 6;
    var widthMinus1 = FlifVarint.Decode(span, ref offset);
    var heightMinus1 = FlifVarint.Decode(span, ref offset);
    var width = widthMinus1 + 1;
    var height = heightMinus1 + 1;

    // Skip frame count for animated (not supported in round-trip, but parse it)
    if (isAnimated) {
      var frameCountMinus2 = FlifVarint.Decode(span, ref offset);
      _ = frameCountMinus2; // not used for single-frame extraction
    }

    // Decode pixel data using the native FLIF codec (MANIAC + range coder)
    var numChannels = (int)channelCount;
    var bytesPerPixel = numChannels * (bitsPerChannel / 8);
    var expectedPixelDataLength = width * height * bytesPerPixel;

    byte[] pixelData;
    try {
      pixelData = _DecodePixelData(data, offset, width, height, numChannels, bitsPerChannel, isInterlaced);
    } catch (Exception ex) when (ex is not InvalidDataException) {
      throw new InvalidDataException("Failed to decompress FLIF pixel data.", ex);
    }

    if (pixelData.Length < expectedPixelDataLength)
      throw new InvalidDataException("Decompressed pixel data is smaller than expected.");

    // Trim to expected size if larger
    if (pixelData.Length > expectedPixelDataLength)
      Array.Resize(ref pixelData, expectedPixelDataLength);

    return new FlifFile {
      Width = width,
      Height = height,
      ChannelCount = channelCount,
      BitsPerChannel = bitsPerChannel,
      IsInterlaced = isInterlaced,
      IsAnimated = isAnimated,
      PixelData = pixelData,
    };
  }

  /// <summary>
  /// Decodes pixel data using the native FLIF codec: range coder + MANIAC trees + transforms.
  /// </summary>
  private static byte[] _DecodePixelData(byte[] data, int dataOffset, int width, int height, int numChannels, int bitsPerChannel, bool isInterlaced) {
    var maxValue = (1 << bitsPerChannel) - 1;
    var decoder = new FlifRangeDecoder(data, dataOffset);

    // Read transform chain
    var transforms = new List<FlifTransform>();
    while (true) {
      var transform = FlifTransform.ReadTransform(decoder, numChannels);
      if (transform == null)
        break;
      transforms.Add(transform);
    }

    // Read the encoding range written by the encoder
    var encodeMin = decoder.DecodeUniform(1023) - 512;
    var encodeMax = decoder.DecodeUniform(1023) - 512;

    // Read MANIAC trees
    var forest = new FlifManiacForest(numChannels);
    forest.ReadTrees(decoder, encodeMin, encodeMax);

    // Decode channel data
    int[][] channels;
    if (isInterlaced) {
      var interlacedDecoder = new FlifInterlacedDecoder(decoder, forest, numChannels, encodeMin, encodeMax);
      channels = interlacedDecoder.DecodeInterlaced(width, height);
    } else {
      var channelDecoder = new FlifChannelDecoder(decoder, forest, numChannels, encodeMin, encodeMax);
      channels = channelDecoder.DecodeNonInterlaced(width, height);
    }

    // Reverse transforms in reverse order
    for (var i = transforms.Count - 1; i >= 0; --i)
      transforms[i].Reverse(channels, width, height);

    // Convert channel planes to interleaved byte array
    return _ChannelsToBytes(channels, width, height, numChannels, bitsPerChannel);
  }

  /// <summary>
  /// Converts separate integer channel planes back to interleaved byte pixel data.
  /// </summary>
  private static byte[] _ChannelsToBytes(int[][] channels, int width, int height, int numChannels, int bitsPerChannel) {
    var bytesPerSample = bitsPerChannel / 8;
    var pixelCount = width * height;
    var result = new byte[pixelCount * numChannels * bytesPerSample];

    if (bitsPerChannel == 8) {
      for (var i = 0; i < pixelCount; ++i)
        for (var c = 0; c < numChannels; ++c)
          result[i * numChannels + c] = (byte)Math.Clamp(channels[c][i], 0, 255);
    } else {
      // 16-bit
      for (var i = 0; i < pixelCount; ++i)
        for (var c = 0; c < numChannels; ++c) {
          var val = (ushort)Math.Clamp(channels[c][i], 0, 65535);
          var byteIdx = (i * numChannels + c) * 2;
          result[byteIdx] = (byte)(val & 0xFF);
          result[byteIdx + 1] = (byte)(val >> 8);
        }
    }

    return result;
  }
}
