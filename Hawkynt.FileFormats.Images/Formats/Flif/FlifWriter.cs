using System;
using System.Collections.Generic;
using FileFormat.Flif.Codec;

namespace FileFormat.Flif;

/// <summary>Assembles FLIF file bytes from pixel data.</summary>
public static class FlifWriter {

  public static byte[] ToBytes(FlifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(
      file.PixelData,
      file.Width,
      file.Height,
      file.ChannelCount,
      file.BitsPerChannel,
      file.IsInterlaced,
      file.IsAnimated
    );
  }

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    FlifChannelCount channelCount,
    int bitsPerChannel,
    bool isInterlaced,
    bool isAnimated
  ) {
    // Calculate header size: 4 (magic) + 1 (header byte) + 1 (bpc) + varint(width-1) + varint(height-1)
    var headerSize = 4 + 1 + 1
                     + FlifVarint.EncodedLength(width - 1)
                     + FlifVarint.EncodedLength(height - 1);

    // Build header
    var header = new byte[headerSize];
    header[0] = (byte)'F';
    header[1] = (byte)'L';
    header[2] = (byte)'I';
    header[3] = (byte)'F';

    // Header byte: bit 4 = interlaced, bit 3 = animated, bits 0-2 = channel count
    byte headerByte = (byte)channelCount;
    if (isInterlaced)
      headerByte |= 0x10;
    if (isAnimated)
      headerByte |= 0x08;
    header[4] = headerByte;

    // Bytes per channel: 0 = 8-bit, 1 = 16-bit
    header[5] = bitsPerChannel switch {
      8 => 0,
      16 => 1,
      _ => throw new ArgumentException($"Unsupported bits per channel: {bitsPerChannel}", nameof(bitsPerChannel))
    };

    // Varint-encoded dimensions (width - 1, height - 1)
    var offset = 6;
    FlifVarint.Encode(header, ref offset, width - 1);
    FlifVarint.Encode(header, ref offset, height - 1);

    // Encode pixel data using the native FLIF codec (MANIAC + range coder)
    var numChannels = (int)channelCount;
    var compressedData = _EncodePixelData(pixelData, width, height, numChannels, bitsPerChannel, isInterlaced);

    // Concatenate header + compressed pixel data
    var result = new byte[headerSize + compressedData.Length];
    header.AsSpan(0, headerSize).CopyTo(result.AsSpan(0));
    compressedData.AsSpan(0, compressedData.Length).CopyTo(result.AsSpan(headerSize));

    return result;
  }

  /// <summary>
  /// Encodes pixel data using the native FLIF codec: transforms + MANIAC trees + range coder.
  /// </summary>
  private static byte[] _EncodePixelData(byte[] pixelData, int width, int height, int numChannels, int bitsPerChannel, bool isInterlaced) {
    var maxValue = (1 << bitsPerChannel) - 1;

    // Convert interleaved byte pixel data to separate integer channel planes
    var channels = _BytesToChannels(pixelData, width, height, numChannels, bitsPerChannel);

    // Apply transforms
    var transforms = new List<FlifTransform>();

    // For RGB/RGBA, apply YCoCg transform for better decorrelation
    if (numChannels >= 3) {
      var ycoCg = new FlifYCoCgTransform();
      ycoCg.Apply(channels, width, height);
      transforms.Add(ycoCg);
    }

    // Apply channel compact transform to shift minimums to zero
    var compact = FlifChannelCompactTransform.Create(numChannels);
    compact.Apply(channels, width, height);
    transforms.Add(compact);

    // Compute bounds after all transforms
    var bounds = FlifBoundsTransform.Create(numChannels);
    bounds.Apply(channels, width, height);
    transforms.Add(bounds);

    // Set up encoder
    var encoder = new FlifRangeEncoder();

    // Write transform chain
    foreach (var transform in transforms)
      transform.WriteTransform(encoder);
    FlifTransform.WriteEndOfChain(encoder);

    // Create MANIAC forest and encode
    var forest = new FlifManiacForest(numChannels);

    // Determine actual value range after transforms
    var encodeMin = 0;
    var encodeMax = maxValue;
    // After YCoCg, values can extend beyond [0, maxValue]
    // Use the bounds from the transforms
    if (transforms.Count > 0) {
      encodeMin = int.MaxValue;
      encodeMax = int.MinValue;
      var pixelCount = width * height;
      for (var c = 0; c < numChannels; ++c) {
        var ch = channels[c];
        for (var i = 0; i < pixelCount; ++i) {
          encodeMin = Math.Min(encodeMin, ch[i]);
          encodeMax = Math.Max(encodeMax, ch[i]);
        }
      }
    }

    // Write the encoding range so the decoder can use the same values
    // Encode as (min + 512) and (max + 512) to handle negative values from YCoCg
    encoder.EncodeUniform(encodeMin + 512, 1023);
    encoder.EncodeUniform(encodeMax + 512, 1023);

    if (isInterlaced) {
      var interlacedEncoder = new FlifInterlacedEncoder(encoder, forest, numChannels, encodeMin, encodeMax);
      interlacedEncoder.EncodeInterlaced(channels, width, height);
    } else {
      var channelEncoder = new FlifChannelEncoder(encoder, forest, numChannels, encodeMin, encodeMax);
      channelEncoder.EncodeNonInterlaced(channels, width, height);
    }

    return encoder.Finish();
  }

  /// <summary>
  /// Converts interleaved byte pixel data to separate integer channel planes.
  /// </summary>
  private static int[][] _BytesToChannels(byte[] pixelData, int width, int height, int numChannels, int bitsPerChannel) {
    var pixelCount = width * height;
    var channels = new int[numChannels][];
    for (var c = 0; c < numChannels; ++c)
      channels[c] = new int[pixelCount];

    if (bitsPerChannel == 8) {
      for (var i = 0; i < pixelCount; ++i)
        for (var c = 0; c < numChannels; ++c) {
          var byteIdx = i * numChannels + c;
          channels[c][i] = byteIdx < pixelData.Length ? pixelData[byteIdx] : 0;
        }
    } else {
      // 16-bit
      for (var i = 0; i < pixelCount; ++i)
        for (var c = 0; c < numChannels; ++c) {
          var byteIdx = (i * numChannels + c) * 2;
          if (byteIdx + 1 < pixelData.Length)
            channels[c][i] = pixelData[byteIdx] | (pixelData[byteIdx + 1] << 8);
        }
    }

    return channels;
  }
}
