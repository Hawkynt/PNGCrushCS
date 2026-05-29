using System;
using System.Collections.Generic;

namespace FileFormat.WebP.Vp8L;

/// <summary>Pure C# VP8L (WebP lossless) decoder pipeline.</summary>
internal static class Vp8LDecoder {

  // ---------------------------------------------------------------------------
  // VP8L distance map: distance codes 1..120 map to (dx, dy) offsets
  // ---------------------------------------------------------------------------
  private static readonly (int dx, int dy)[] _DISTANCE_MAP = [
    (0, 1), (1, 0), (1, 1), (-1, 1), (0, 2), (2, 0), (1, 2), (-1, 2), (2, 1), (-2, 1),
    (2, 2), (-2, 2), (0, 3), (3, 0), (1, 3), (-1, 3), (3, 1), (-3, 1), (2, 3), (-2, 3),
    (3, 2), (-3, 2), (0, 4), (4, 0), (1, 4), (-1, 4), (4, 1), (-4, 1), (3, 3), (-3, 3),
    (2, 4), (-2, 4), (4, 2), (-4, 2), (0, 5), (3, 4), (-3, 4), (4, 3), (-4, 3), (5, 0),
    (1, 5), (-1, 5), (5, 1), (-5, 1), (2, 5), (-2, 5), (5, 2), (-5, 2), (4, 4), (-4, 4),
    (3, 5), (-3, 5), (5, 3), (-5, 3), (0, 6), (6, 0), (1, 6), (-1, 6), (6, 1), (-6, 1),
    (2, 6), (-2, 6), (6, 2), (-6, 2), (4, 5), (-4, 5), (5, 4), (-5, 4), (3, 6), (-3, 6),
    (6, 3), (-6, 3), (0, 7), (7, 0), (1, 7), (-1, 7), (5, 5), (-5, 5), (7, 1), (-7, 1),
    (4, 6), (-4, 6), (6, 4), (-6, 4), (2, 7), (-2, 7), (7, 2), (-7, 2), (3, 7), (-3, 7),
    (7, 3), (-7, 3), (5, 6), (-5, 6), (6, 5), (-6, 5), (0, 8), (8, 0), (1, 8), (-1, 8),
    (8, 1), (-8, 1), (4, 7), (-4, 7), (7, 4), (-7, 4), (2, 8), (-2, 8), (8, 2), (-8, 2),
    (6, 6), (-6, 6), (5, 7), (-5, 7), (7, 5), (-7, 5), (3, 8), (-3, 8), (8, 3), (-8, 3),
  ];

  /// <summary>Decode VP8L bitstream into RGBA byte array.</summary>
  /// <param name="vp8lData">Raw VP8L chunk data (starting with the 0x2F signature byte).</param>
  /// <param name="width">Image width from the VP8L header.</param>
  /// <param name="height">Image height from the VP8L header.</param>
  /// <param name="hasAlpha">Whether the image has alpha from the VP8L header.</param>
  /// <returns>RGBA byte array of size width * height * 4.</returns>
  public static byte[] Decode(byte[] vp8lData, int width, int height, bool hasAlpha) {
    // VP8L data: 1 byte signature (0x2F) + 4 bytes bitfield header = 5 bytes
    // The bit reader starts after the 5-byte header
    var reader = new Vp8LBitReader(vp8lData, 5);

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
    var pixels = _DecodeImageData(reader, effectiveWidth, effectiveHeight, fullSize);

    // Apply inverse transforms in reverse order
    for (var i = transforms.Count - 1; i >= 0; --i) {
      if (transforms[i] is Vp8LColorIndexingTransform ci)
        ci.InverseTransform(pixels, ci.OriginalWidth, effectiveHeight);
      else
        transforms[i].InverseTransform(pixels, width, height);
    }

    // Convert ARGB uint[] to RGBA byte[]
    return _ArgbToRgba(pixels, width, height, hasAlpha);
  }

  /// <summary>Read a sub-resolution image (used for transform data and meta-Huffman images).</summary>
  private static uint[] _ReadSubResolutionImage(Vp8LBitReader reader, int width, int height)
    => _DecodeImageData(reader, width, height, width * height);

  /// <summary>Decode image data using Huffman-coded LZ77.</summary>
  /// <param name="reader">Bit reader positioned at the image data.</param>
  /// <param name="width">Image width (may be packed for color-indexed images).</param>
  /// <param name="height">Image height.</param>
  /// <param name="minArraySize">Minimum size of the output array (may be larger than width*height for color-indexed unpacking).</param>
  private static uint[] _DecodeImageData(Vp8LBitReader reader, int width, int height, int minArraySize) {
    var numPixels = width * height;
    var pixels = new uint[Math.Max(numPixels, minArraySize)];

    // Read whether meta-Huffman is used
    var useMeta = reader.ReadBits(1);

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

    // Read color cache parameters
    var colorCacheBits = 0;
    var useColorCache = reader.ReadBits(1);
    if (useColorCache == 1) {
      colorCacheBits = (int)reader.ReadBits(4);
      if (colorCacheBits < 1 || colorCacheBits > 11)
        throw new InvalidOperationException($"Invalid color cache bits: {colorCacheBits}");
    }

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

    if (distCode <= _DISTANCE_MAP.Length) {
      var (dx, dy) = _DISTANCE_MAP[distCode - 1];
      var dist = dx + dy * width;
      return dist >= 1 ? dist : 1;
    }

    return distCode - (int)_DISTANCE_MAP.Length;
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
