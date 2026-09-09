using System;
using System.Collections.Generic;

namespace FileFormat.WebP.Vp8L;

/// <summary>Pure C# VP8L (WebP lossless) decoder pipeline.</summary>
internal static class Vp8LDecoder {

  /// <summary>
  /// Distance codes 1..120 name a nearby pixel by its position rather than by a raw backwards
  /// distance, so that a match a row or two up costs a short code. This is the packing libwebp
  /// calls <c>kCodeToPlane</c>: the high nibble is how many rows back, and the low nibble is eight
  /// minus how many columns across, which is why the column offsets run -7..8 rather than
  /// symmetrically. The order is a fixed table and not a rule — it climbs roughly by distance, but
  /// the tail does not continue any pattern the head suggests, so it must be transcribed. It is
  /// kept in this packed form, and unpacked below, precisely so that nobody can extend it by
  /// guessing the pattern: getting entries wrong here corrupts pictures other encoders wrote while
  /// leaving everything this package writes untouched, because our own encoder never emits these
  /// codes at all.
  /// </summary>
  private static ReadOnlySpan<byte> _CodeToPlane =>
  [
    0x18, 0x07, 0x17, 0x19, 0x28, 0x06, 0x27, 0x29, 0x16, 0x1a,
    0x26, 0x2a, 0x38, 0x05, 0x37, 0x39, 0x15, 0x1b, 0x36, 0x3a,
    0x25, 0x2b, 0x48, 0x04, 0x47, 0x49, 0x14, 0x1c, 0x35, 0x3b,
    0x46, 0x4a, 0x24, 0x2c, 0x58, 0x45, 0x4b, 0x34, 0x3c, 0x03,
    0x57, 0x59, 0x13, 0x1d, 0x56, 0x5a, 0x23, 0x2d, 0x44, 0x4c,
    0x55, 0x5b, 0x33, 0x3d, 0x68, 0x02, 0x67, 0x69, 0x12, 0x1e,
    0x66, 0x6a, 0x22, 0x2e, 0x54, 0x5c, 0x43, 0x4d, 0x65, 0x6b,
    0x32, 0x3e, 0x78, 0x01, 0x77, 0x79, 0x53, 0x5d, 0x11, 0x1f,
    0x64, 0x6c, 0x42, 0x4e, 0x76, 0x7a, 0x21, 0x2f, 0x75, 0x7b,
    0x31, 0x3f, 0x63, 0x6d, 0x52, 0x5e, 0x00, 0x74, 0x7c, 0x41,
    0x4f, 0x10, 0x20, 0x62, 0x6e, 0x30, 0x73, 0x7d, 0x51, 0x5f,
    0x40, 0x72, 0x7e, 0x61, 0x6f, 0x50, 0x71, 0x7f, 0x60, 0x70,
  ];

  /// <summary>How many distance codes carry a position rather than a plain distance.</summary>
  internal const int CodeToPlaneCount = 120;

  /// <summary>Unpacks distance code <paramref name="planeCode"/> into its column and row offset.</summary>
  internal static (int Dx, int Dy) CodeToOffset(int planeCode) {
    var packed = _CodeToPlane[planeCode - 1];
    return (8 - (packed & 0xF), packed >> 4);
  }

  /// <summary>Decode VP8L bitstream into RGBA byte array.</summary>
  /// <param name="vp8lData">Raw VP8L chunk data (starting with the 0x2F signature byte).</param>
  /// <param name="width">Image width from the VP8L header.</param>
  /// <param name="height">Image height from the VP8L header.</param>
  /// <param name="hasAlpha">Whether the image has alpha from the VP8L header.</param>
  /// <returns>RGBA byte array of size width * height * 4.</returns>
  public static byte[] Decode(byte[] vp8lData, int width, int height, bool hasAlpha)
    // VP8L data: 1 byte signature (0x2F) + 4 bytes bitfield header = 5 bytes.
    // The bit reader starts after the 5-byte header.
    => _ArgbToRgba(DecodeArgbStream(vp8lData, 5, width, height), width, height, hasAlpha);

  /// <summary>Decodes a bare VP8L image stream — transforms, Huffman codes and all — from a bit
  /// position the caller names, into ARGB pixels.</summary>
  /// <remarks>
  /// The stream a VP8L chunk holds is preceded by five bytes stating a signature and the size, and
  /// everything after those is the stream proper. An ALPH chunk with compression method 1 holds the
  /// same stream with no such preamble: it is one byte of flags and then the bits, and the size
  /// comes from the picture the alpha belongs to. Splitting the entry point is what lets one decoder
  /// serve both instead of the alpha path growing a second, thinner copy of it.
  /// </remarks>
  /// <param name="data">The buffer holding the stream.</param>
  /// <param name="byteOffset">Where in it the stream's first bit sits.</param>
  /// <param name="width">Image width, from whatever states it for this stream.</param>
  /// <param name="height">Image height, likewise.</param>
  public static uint[] DecodeArgbStream(byte[] data, int byteOffset, int width, int height) {
    var reader = new Vp8LBitReader(data, byteOffset);

    // Read transforms
    var transforms = new List<Vp8LTransform>();
    var effectiveWidth = width;
    var effectiveHeight = height;
    var usedTransforms = new bool[4];

    while (reader.ReadBits(1) == 1) {
      var transformType = (Vp8LTransformType)reader.ReadBits(2);
      if (usedTransforms[(int)transformType])
        throw new InvalidOperationException($"Duplicate transform: {transformType}");
      usedTransforms[(int)transformType] = true;

      switch (transformType) {
        case Vp8LTransformType.Predictor: {
          var blockBits = (int)reader.ReadBits(3) + 2;
          var transformData = _ReadSubResolutionImage(reader, _DivRoundUp(effectiveWidth, 1 << blockBits), _DivRoundUp(effectiveHeight, 1 << blockBits));
          transforms.Add(new Vp8LPredictorTransform(transformData, blockBits));
          break;
        }
        case Vp8LTransformType.CrossColor: {
          var blockBits = (int)reader.ReadBits(3) + 2;
          var transformData = _ReadSubResolutionImage(reader, _DivRoundUp(effectiveWidth, 1 << blockBits), _DivRoundUp(effectiveHeight, 1 << blockBits));
          transforms.Add(new Vp8LCrossColorTransform(transformData, blockBits));
          break;
        }
        case Vp8LTransformType.SubtractGreen:
          transforms.Add(new Vp8LSubtractGreenTransform());
          break;
        case Vp8LTransformType.ColorIndexing: {
          var paletteSize = (int)reader.ReadBits(8) + 1;
          var palette = _ReadSubResolutionImage(reader, paletteSize, 1);

          // Inverse delta-encode palette (palette pixels are delta-encoded)
          for (var i = 1; i < paletteSize; ++i)
            palette[i] = _AddPixels(palette[i], palette[i - 1]);

          var colorIndexing = new Vp8LColorIndexingTransform(palette, effectiveWidth);
          effectiveWidth = colorIndexing.EncodedWidth;
          transforms.Add(colorIndexing);
          break;
        }
      }
    }

    // Decode the main image into an array large enough for the final output
    var fullSize = width * height;
    var pixels = _DecodeImageData(reader, effectiveWidth, effectiveHeight, fullSize, allowMetaHuffman: true);

    // Apply inverse transforms in reverse order
    for (var i = transforms.Count - 1; i >= 0; --i) {
      if (transforms[i] is Vp8LColorIndexingTransform ci)
        ci.InverseTransform(pixels, ci.OriginalWidth, effectiveHeight);
      else
        transforms[i].InverseTransform(pixels, width, height);
    }

    return pixels;
  }

  /// <summary>Read a sub-resolution image (used for transform data and meta-Huffman images).</summary>
  private static uint[] _ReadSubResolutionImage(Vp8LBitReader reader, int width, int height)
    => _DecodeImageData(reader, width, height, width * height, allowMetaHuffman: false);

  /// <summary>Decode image data using Huffman-coded LZ77.</summary>
  /// <param name="reader">Bit reader positioned at the image data.</param>
  /// <param name="width">Image width (may be packed for color-indexed images).</param>
  /// <param name="height">Image height.</param>
  /// <param name="minArraySize">Minimum size of the output array (may be larger than width*height for color-indexed unpacking).</param>
  private static uint[] _DecodeImageData(Vp8LBitReader reader, int width, int height, int minArraySize, bool allowMetaHuffman) {
    var numPixels = width * height;
    var pixels = new uint[Math.Max(numPixels, minArraySize)];

    // Field order per the VP8L bitstream: colour-cache info precedes the meta-Huffman flag, and
    // sub-resolution images (transform data, the entropy image) carry no meta-Huffman flag at all.
    var colorCacheBits = 0;
    if (reader.ReadBits(1) == 1) {
      colorCacheBits = (int)reader.ReadBits(4);
      if (colorCacheBits < 1 || colorCacheBits > 11)
        throw new InvalidOperationException($"Invalid color cache bits: {colorCacheBits}");
    }

    var useMeta = allowMetaHuffman ? reader.ReadBits(1) : 0u;

    int numHuffmanGroups;
    uint[]? metaImage = null;
    var metaBlockBits = 0;
    var metaBlocksPerRow = 0;

    if (useMeta == 1) {
      metaBlockBits = (int)reader.ReadBits(3) + 2;
      var metaWidth = _DivRoundUp(width, 1 << metaBlockBits);
      var metaHeight = _DivRoundUp(height, 1 << metaBlockBits);
      metaImage = _ReadSubResolutionImage(reader, metaWidth, metaHeight);
      metaBlocksPerRow = metaWidth;

      // Find max huffman group index
      numHuffmanGroups = 0;
      for (var i = 0; i < metaImage.Length; ++i) {
        var groupIndex = (int)((metaImage[i] >> 8) & 0xFFFF); // bits 8..23 of the meta pixel (green+red)
        if (groupIndex >= numHuffmanGroups)
          numHuffmanGroups = groupIndex + 1;
      }
    } else {
      numHuffmanGroups = 1;
    }

    // Alphabet sizes for the 5 trees:
    // Green + length: 256 (literals) + 24 (length codes) + color cache size
    // Red: 256
    // Blue: 256
    // Alpha: 256
    // Distance: 40

    var colorCacheSize = colorCacheBits > 0 ? 1 << colorCacheBits : 0;
    var greenAlphabetSize = 256 + 24 + colorCacheSize;

    // Read Huffman tree groups
    var groups = new Vp8LHuffmanTree[numHuffmanGroups][];
    for (var g = 0; g < numHuffmanGroups; ++g) {
      groups[g] = new Vp8LHuffmanTree[5];
      groups[g][0] = Vp8LHuffmanTree.ReadTree(reader, greenAlphabetSize); // green + length prefix + color cache
      groups[g][1] = Vp8LHuffmanTree.ReadTree(reader, 256);               // red
      groups[g][2] = Vp8LHuffmanTree.ReadTree(reader, 256);               // blue
      groups[g][3] = Vp8LHuffmanTree.ReadTree(reader, 256);               // alpha
      groups[g][4] = Vp8LHuffmanTree.ReadTree(reader, 40);                // distance prefix
    }

    // Initialize color cache
    uint[]? colorCache = colorCacheSize > 0 ? new uint[colorCacheSize] : null;
    var colorCacheMask = colorCacheSize - 1;

    // Decode pixels
    var pos = 0;
    while (pos < numPixels) {
      // Determine Huffman group
      var groupIndex = 0;
      if (metaImage != null) {
        var y = pos / width;
        var x = pos % width;
        var metaX = x >> metaBlockBits;
        var metaY = y >> metaBlockBits;
        var metaIdx = metaY * metaBlocksPerRow + metaX;
        if (metaIdx < metaImage.Length)
          groupIndex = (int)((metaImage[metaIdx] >> 8) & 0xFFFF);
      }

      var group = groups[groupIndex];
      var greenSymbol = group[0].ReadSymbol(reader);

      if (greenSymbol < 256) {
        // Literal pixel
        var red = (uint)group[1].ReadSymbol(reader);
        var blue = (uint)group[2].ReadSymbol(reader);
        var alpha = (uint)group[3].ReadSymbol(reader);
        var pixel = (alpha << 24) | (red << 16) | ((uint)greenSymbol << 8) | blue;
        pixels[pos] = pixel;

        if (colorCache != null)
          colorCache[_ColorCacheHash(pixel, colorCacheBits) & colorCacheMask] = pixel;

        ++pos;
      } else if (greenSymbol < 256 + 24) {
        // Length-distance backref
        var lengthCode = greenSymbol - 256;
        var length = _DecodeLengthOrDistance(reader, lengthCode);

        var distanceCode = group[4].ReadSymbol(reader);
        var distanceRaw = _DecodeLengthOrDistance(reader, distanceCode);
        var distance = _PlaneCodeToDistance(width, distanceRaw);

        if (distance > pos)
          distance = pos; // clamp to available

        var srcPos = pos - distance;
        for (var i = 0; i < length && pos < numPixels; ++i) {
          var pixel = pixels[srcPos + (i % distance)];
          pixels[pos] = pixel;

          if (colorCache != null)
            colorCache[_ColorCacheHash(pixel, colorCacheBits) & colorCacheMask] = pixel;

          ++pos;
        }
      } else if (greenSymbol < 256 + 24 + colorCacheSize) {
        // Color cache lookup
        var cacheIndex = greenSymbol - 256 - 24;
        var pixel = colorCache![cacheIndex];
        pixels[pos] = pixel;

        colorCache[_ColorCacheHash(pixel, colorCacheBits) & colorCacheMask] = pixel;
        ++pos;
      } else {
        throw new InvalidOperationException($"Unexpected green symbol: {greenSymbol}");
      }
    }

    return pixels;
  }

  /// <summary>Decode a length or distance value from its prefix code + extra bits.</summary>
  private static int _DecodeLengthOrDistance(Vp8LBitReader reader, int prefixCode) {
    if (prefixCode < 4)
      return prefixCode + 1;

    var extraBits = (prefixCode - 2) >> 1;
    var offset = (2 + (prefixCode & 1)) << extraBits;
    return offset + (int)reader.ReadBits(extraBits) + 1;
  }

  /// <summary>Convert a VP8L distance code (1-based) to a linear pixel distance using the 2D distance map.</summary>
  private static int _PlaneCodeToDistance(int width, int distCode) {
    if (distCode <= 0)
      return 1;

    if (distCode <= CodeToPlaneCount) {
      var (dx, dy) = CodeToOffset(distCode);
      var dist = dx + dy * width;
      return dist >= 1 ? dist : 1;
    }

    return distCode - CodeToPlaneCount;
  }

  /// <summary>VP8L color cache hash function (0x1E35A7BD multiplicative hash).</summary>
  private static int _ColorCacheHash(uint argb, int bits)
    => (int)((argb * 0x1E35A7BD) >> (32 - bits));

  /// <summary>Add two ARGB pixels component-wise (mod 256).</summary>
  private static uint _AddPixels(uint a, uint b) {
    var alpha = ((a >> 24) + (b >> 24)) & 0xFF;
    var red = (((a >> 16) & 0xFF) + ((b >> 16) & 0xFF)) & 0xFF;
    var green = (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF)) & 0xFF;
    var blue = ((a & 0xFF) + (b & 0xFF)) & 0xFF;
    return (alpha << 24) | (red << 16) | (green << 8) | blue;
  }

  /// <summary>Convert ARGB uint[] to RGBA byte[] for RawImage compatibility.</summary>
  private static byte[] _ArgbToRgba(uint[] pixels, int width, int height, bool hasAlpha) {
    var count = width * height;
    var result = new byte[count * 4];
    for (var i = 0; i < count; ++i) {
      var argb = pixels[i];
      var offset = i * 4;
      result[offset] = (byte)((argb >> 16) & 0xFF);     // R
      result[offset + 1] = (byte)((argb >> 8) & 0xFF);  // G
      result[offset + 2] = (byte)(argb & 0xFF);          // B
      result[offset + 3] = hasAlpha ? (byte)((argb >> 24) & 0xFF) : (byte)0xFF; // A
    }

    return result;
  }

  private static int _DivRoundUp(int num, int den) => (num + den - 1) / den;
}
