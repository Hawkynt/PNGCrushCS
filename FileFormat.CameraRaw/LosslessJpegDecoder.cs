using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.CameraRaw;

/// <summary>Decodes lossless JPEG (ITU-T T.81 process 14, SOF3 marker 0xFFC3) used by Canon CR2, Nikon NEF, and DNG files for raw CFA sensor data.</summary>
internal static class LosslessJpegDecoder {

  // JPEG markers
  private const ushort _SOI = 0xFFD8;
  private const ushort _EOI = 0xFFD9;
  private const ushort _SOF3 = 0xFFC3;
  private const ushort _DHT = 0xFFC4;
  private const ushort _SOS = 0xFFDA;
  private const ushort _DRI = 0xFFDD;

  /// <summary>Result of lossless JPEG decoding.</summary>
  internal sealed class DecodedImage {
    public int Width;
    public int Height;
    public int Precision;
    public int ComponentCount;
    public ushort[] Samples = [];
  }

  /// <summary>Single component descriptor from the SOF3 frame header.</summary>
  private readonly struct ComponentInfo {
    public readonly byte Id;
    public readonly byte HorizontalSampling;
    public readonly byte VerticalSampling;

    public ComponentInfo(byte id, byte hSampling, byte vSampling) {
      Id = id;
      HorizontalSampling = hSampling;
      VerticalSampling = vSampling;
    }
  }

  /// <summary>Huffman table node for decoding variable-length codes.</summary>
  internal sealed class HuffmanTable {
    // For fast lookup: minCode[i] = minimum code of length i+1, maxCode[i] = maximum code of length i+1
    public readonly int[] MinCode = new int[17];
    public readonly int[] MaxCode = new int[17];
    public readonly int[] ValPtr = new int[17]; // index into Values for codes of length i+1
    public readonly byte[] Values = new byte[256];
    public int ValueCount;
  }

  /// <summary>Bit reader for the JPEG entropy-coded segment, handling byte stuffing (0xFF00 -> 0xFF).</summary>
  private ref struct BitReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;
    private uint _buffer;
    private int _bitsLeft;

    public BitReader(ReadOnlySpan<byte> data, int startOffset) {
      _data = data;
      _pos = startOffset;
      _buffer = 0;
      _bitsLeft = 0;
    }

    public int Position => _pos;

    public int ReadBits(int count) {
      while (_bitsLeft < count) {
        if (_pos >= _data.Length)
          throw new InvalidDataException("Unexpected end of JPEG data.");

        var b = _data[_pos++];
        if (b == 0xFF) {
          if (_pos < _data.Length && _data[_pos] == 0x00)
            ++_pos; // byte stuffing: skip the 0x00
          else {
            // This is a marker; push back and stop
            --_pos;
            // Fill with zeros to allow finishing
            _buffer = (_buffer << 8);
            _bitsLeft += 8;
            continue;
          }
        }

        _buffer = (_buffer << 8) | b;
        _bitsLeft += 8;
      }

      _bitsLeft -= count;
      return (int)((_buffer >> _bitsLeft) & ((1u << count) - 1));
    }

    public int PeekBits(int count) {
      while (_bitsLeft < count) {
        if (_pos >= _data.Length) {
          _buffer = (_buffer << 8);
          _bitsLeft += 8;
          continue;
        }

        var b = _data[_pos++];
        if (b == 0xFF) {
          if (_pos < _data.Length && _data[_pos] == 0x00)
            ++_pos;
          else {
            --_pos;
            _buffer = (_buffer << 8);
            _bitsLeft += 8;
            continue;
          }
        }

        _buffer = (_buffer << 8) | b;
        _bitsLeft += 8;
      }

      return (int)((_buffer >> (_bitsLeft - count)) & ((1u << count) - 1));
    }

    /// <summary>Check if the next bytes form a restart marker (0xFFD0-0xFFD7) and skip it.</summary>
    public bool TryConsumeRestartMarker() {
      // Discard remaining bits in current byte
      _bitsLeft = 0;
      _buffer = 0;

      if (_pos + 1 >= _data.Length)
        return false;

      if (_data[_pos] == 0xFF && _data[_pos + 1] >= 0xD0 && _data[_pos + 1] <= 0xD7) {
        _pos += 2;
        return true;
      }

      return false;
    }
  }

  /// <summary>Decode a lossless JPEG bitstream (SOF3).</summary>
  /// <param name="data">Full JPEG bitstream starting with SOI.</param>
  /// <returns>Decoded image with 16-bit samples.</returns>
  public static DecodedImage Decode(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for lossless JPEG.");

    var span = data.AsSpan();
    var offset = 0;

    // Verify SOI
    if (_ReadUInt16BE(span, ref offset) != _SOI)
      throw new InvalidDataException("Missing JPEG SOI marker.");

    // Parse markers
    var dcTables = new Dictionary<int, HuffmanTable>();
    int width = 0, height = 0, precision = 0;
    var components = Array.Empty<ComponentInfo>();
    var restartInterval = 0;
    var sosOffset = -1;
    var predictor = 1;
    var pointTransform = 0;
    byte[] sosComponentTableIds = [];

    while (offset + 1 < data.Length) {
      var marker = _ReadUInt16BE(span, ref offset);

      if (marker == _EOI)
        break;

      if (marker == _SOS) {
        var sosLength = _ReadUInt16BE(span, ref offset);
        var numSosComponents = data[offset++];
        sosComponentTableIds = new byte[numSosComponents];
        for (var i = 0; i < numSosComponents; ++i) {
          var _componentSelector = data[offset++]; // component selector (unused, we use order)
          var tableSpec = data[offset++];
          sosComponentTableIds[i] = (byte)(tableSpec >> 4); // DC table selector
        }

        predictor = data[offset++]; // Ss = predictor selection
        var _se = data[offset++]; // Se (unused in lossless)
        var ahAl = data[offset++]; // Ah (high nibble) | Al (low nibble)
        pointTransform = ahAl & 0x0F;

        sosOffset = offset;
        break;
      }

      if ((marker & 0xFF00) != 0xFF00)
        continue;

      // Skip non-parametric markers
      if (marker == _SOI || (marker >= 0xFFD0 && marker <= 0xFFD7))
        continue;

      if (offset + 1 >= data.Length)
        break;

      var length = _ReadUInt16BE(span, ref offset);
      var segmentEnd = offset + length - 2;

      switch (marker) {
        case _SOF3: {
          precision = data[offset++];
          height = (data[offset] << 8) | data[offset + 1];
          offset += 2;
          width = (data[offset] << 8) | data[offset + 1];
          offset += 2;
          var numComponents = data[offset++];
          components = new ComponentInfo[numComponents];
          for (var i = 0; i < numComponents; ++i) {
            var id = data[offset++];
            var samplingFactors = data[offset++];
            var _qt = data[offset++]; // quantization table (unused in lossless)
            components[i] = new(id, (byte)(samplingFactors >> 4), (byte)(samplingFactors & 0x0F));
          }

          break;
        }
        case _DHT:
          _ParseDht(data, offset, segmentEnd, dcTables);
          offset = segmentEnd;
          break;
        case _DRI:
          restartInterval = (data[offset] << 8) | data[offset + 1];
          offset = segmentEnd;
          break;
        default:
          offset = segmentEnd;
          break;
      }
    }

    if (sosOffset < 0 || components.Length == 0)
      throw new InvalidDataException("Lossless JPEG missing required SOF3/SOS markers.");

    if (predictor < 0 || predictor > 7)
      throw new InvalidDataException($"Invalid lossless JPEG predictor: {predictor}.");

    // Determine if we have interleaved components (all components in one scan)
    var nComp = components.Length;
    var totalSamples = (long)width * height * nComp;
    var output = new ushort[totalSamples];

    // Build table mapping for SOS component order
    var tables = new HuffmanTable[nComp];
    for (var i = 0; i < nComp; ++i) {
      var tableId = i < sosComponentTableIds.Length ? sosComponentTableIds[i] : 0;
      if (!dcTables.TryGetValue(tableId, out var ht))
        throw new InvalidDataException($"Huffman table {tableId} not found.");
      tables[i] = ht;
    }

    // Decode the entropy-coded segment
    var reader = new BitReader(span, sosOffset);
    var halfRange = 1 << (precision - pointTransform - 1);
    var maxVal = (1 << precision) - 1;
    // restartInterval is passed to the per-component/interleaved decode methods

    // For multi-component interleaved: samples are interleaved by MCU
    // For 1-component: straightforward raster order
    if (nComp == 1) {
      _DecodeSingleComponent(ref reader, output, width, height, precision, predictor, pointTransform, tables[0], restartInterval, maxVal, halfRange);
    } else {
      _DecodeInterleaved(ref reader, output, width, height, nComp, precision, predictor, pointTransform, tables, components, restartInterval, maxVal, halfRange);
    }

    return new() {
      Width = width,
      Height = height,
      Precision = precision,
      ComponentCount = nComp,
      Samples = output,
    };
  }

  /// <summary>Decode a single-component lossless JPEG in raster order.</summary>
  private static void _DecodeSingleComponent(ref BitReader reader, ushort[] output, int width, int height, int precision, int predictor, int pointTransform, HuffmanTable table, int restartInterval, int maxVal, int halfRange) {
    var mcuCount = 0;
    var nextRestart = restartInterval > 0 ? restartInterval : int.MaxValue;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (restartInterval > 0 && mcuCount == nextRestart) {
          reader.TryConsumeRestartMarker();
          mcuCount = 0;
          nextRestart = restartInterval;
          // Reset prediction to use predictor 0 for this row start
        }

        var predicted = _Predict(output, x, y, width, 1, 0, predictor, mcuCount == 0 && y == 0, x == 0 || (restartInterval > 0 && mcuCount == 0), y == 0, halfRange, pointTransform);
        var diff = _DecodeHuffmanDifference(ref reader, table, precision);
        var value = (predicted + diff) & maxVal;
        output[y * width + x] = (ushort)value;
        ++mcuCount;
      }
  }

  /// <summary>Decode interleaved multi-component lossless JPEG.</summary>
  private static void _DecodeInterleaved(ref BitReader reader, ushort[] output, int width, int height, int nComp, int precision, int predictor, int pointTransform, HuffmanTable[] tables, ComponentInfo[] components, int restartInterval, int maxVal, int halfRange) {
    var mcuCount = 0;
    var nextRestart = restartInterval > 0 ? restartInterval : int.MaxValue;

    // Check for non-1x1 sampling factors (e.g., Canon CR2 uses 2x1 for some components)
    var maxH = 1;
    var maxV = 1;
    for (var c = 0; c < nComp; ++c) {
      if (components[c].HorizontalSampling > maxH)
        maxH = components[c].HorizontalSampling;
      if (components[c].VerticalSampling > maxV)
        maxV = components[c].VerticalSampling;
    }

    if (maxH == 1 && maxV == 1) {
      // Simple interleaved: each MCU is one sample per component
      var mcuWidth = width;
      var mcuHeight = height;

      for (var y = 0; y < mcuHeight; ++y)
        for (var x = 0; x < mcuWidth; ++x) {
          if (restartInterval > 0 && mcuCount == nextRestart) {
            reader.TryConsumeRestartMarker();
            mcuCount = 0;
          }

          for (var c = 0; c < nComp; ++c) {
            var predicted = _Predict(output, x, y, width, nComp, c, predictor,
              mcuCount == 0 && y == 0,
              x == 0 || (restartInterval > 0 && mcuCount == 0),
              y == 0,
              halfRange, pointTransform);
            var diff = _DecodeHuffmanDifference(ref reader, tables[c], precision);
            var value = (predicted + diff) & maxVal;
            output[(y * width + x) * nComp + c] = (ushort)value;
          }

          ++mcuCount;
        }
    } else {
      // Subsampled interleaved: MCU contains multiple samples per component
      var mcuPixelsH = maxH;
      var mcuPixelsV = maxV;
      var mcuCols = (width + mcuPixelsH - 1) / mcuPixelsH;
      var mcuRows = (height + mcuPixelsV - 1) / mcuPixelsV;

      for (var mcuRow = 0; mcuRow < mcuRows; ++mcuRow)
        for (var mcuCol = 0; mcuCol < mcuCols; ++mcuCol) {
          if (restartInterval > 0 && mcuCount == nextRestart) {
            reader.TryConsumeRestartMarker();
            mcuCount = 0;
          }

          for (var c = 0; c < nComp; ++c) {
            var hSamp = components[c].HorizontalSampling;
            var vSamp = components[c].VerticalSampling;

            for (var sv = 0; sv < vSamp; ++sv)
              for (var sh = 0; sh < hSamp; ++sh) {
                var x = mcuCol * mcuPixelsH + sh;
                var y = mcuRow * mcuPixelsV + sv;
                if (x >= width || y >= height)
                  continue;

                var predicted = _Predict(output, x, y, width, nComp, c, predictor,
                  mcuCount == 0 && mcuRow == 0,
                  x == 0 || (restartInterval > 0 && mcuCount == 0 && sh == 0),
                  y == 0,
                  halfRange, pointTransform);
                var diff = _DecodeHuffmanDifference(ref reader, tables[c], precision);
                var value = (predicted + diff) & maxVal;
                output[(y * width + x) * nComp + c] = (ushort)value;
              }
          }

          ++mcuCount;
        }
    }
  }

  /// <summary>Compute the prediction value for position (x, y) using one of the 7 ITU-T T.81 prediction modes.</summary>
  private static int _Predict(ushort[] output, int x, int y, int width, int nComp, int compIdx, int predictor, bool isFirstMcu, bool isLeftEdge, bool isTopEdge, int halfRange, int pointTransform) {
    // First sample in the scan: use 2^(P-Pt-1)
    if (isFirstMcu && x == 0 && y == 0)
      return halfRange;

    // First row: use Ra (left neighbor) regardless of predictor
    if (isTopEdge && y == 0) {
      if (isLeftEdge || x == 0)
        return halfRange;
      return output[(y * width + x - 1) * nComp + compIdx];
    }

    // First column: use Rb (above neighbor) regardless of predictor
    if (isLeftEdge || x == 0) {
      if (y == 0)
        return halfRange;
      return output[((y - 1) * width + x) * nComp + compIdx];
    }

    var ra = (int)output[(y * width + x - 1) * nComp + compIdx]; // left
    var rb = (int)output[((y - 1) * width + x) * nComp + compIdx]; // above
    var rc = (int)output[((y - 1) * width + x - 1) * nComp + compIdx]; // above-left

    return predictor switch {
      0 => 0, // No prediction
      1 => ra, // Left
      2 => rb, // Above
      3 => rc, // Above-left
      4 => ra + rb - rc, // Linear: Ra + Rb - Rc
      5 => ra + ((rb - rc) >> 1), // Ra + (Rb - Rc) / 2
      6 => rb + ((ra - rc) >> 1), // Rb + (Ra - Rc) / 2
      7 => (ra + rb) >> 1, // Average: (Ra + Rb) / 2
      _ => ra
    };
  }

  /// <summary>Decode one Huffman-coded difference value: read category (SSSS), then read SSSS additional bits for signed difference.</summary>
  private static int _DecodeHuffmanDifference(ref BitReader reader, HuffmanTable table, int precision) {
    var category = _DecodeHuffmanSymbol(ref reader, table);

    if (category == 0)
      return 0;

    if (category == 16)
      return -32768; // special case for 16-bit

    var additionalBits = reader.ReadBits(category);

    // Convert to signed: if high bit is 0, value is negative
    if (additionalBits < (1 << (category - 1)))
      return additionalBits - (1 << category) + 1;

    return additionalBits;
  }

  /// <summary>Decode one Huffman symbol using the table's code-length structure.</summary>
  private static int _DecodeHuffmanSymbol(ref BitReader reader, HuffmanTable table) {
    var code = 0;
    for (var length = 1; length <= 16; ++length) {
      code = (code << 1) | reader.ReadBits(1);
      if (code <= table.MaxCode[length]) {
        var index = table.ValPtr[length] + code - table.MinCode[length];
        if (index >= 0 && index < table.ValueCount)
          return table.Values[index];
      }
    }

    throw new InvalidDataException("Invalid Huffman code in lossless JPEG.");
  }

  /// <summary>Parse one or more DHT (Define Huffman Table) segments.</summary>
  internal static void _ParseDht(byte[] data, int offset, int endOffset, Dictionary<int, HuffmanTable> tables) {
    while (offset < endOffset) {
      var tableInfo = data[offset++];
      var tableClass = tableInfo >> 4; // 0 = DC (used in lossless)
      var tableId = tableInfo & 0x0F;

      var ht = new HuffmanTable();
      var counts = new int[17]; // counts[1..16] = number of codes of each length
      var totalValues = 0;
      for (var i = 1; i <= 16; ++i) {
        counts[i] = data[offset++];
        totalValues += counts[i];
      }

      ht.ValueCount = totalValues;
      for (var i = 0; i < totalValues && i < 256; ++i)
        ht.Values[i] = data[offset++];

      // Build min/max code tables for decoding
      var code = 0;
      var valueIndex = 0;
      for (var length = 1; length <= 16; ++length) {
        ht.ValPtr[length] = valueIndex;
        if (counts[length] > 0) {
          ht.MinCode[length] = code;
          ht.MaxCode[length] = code + counts[length] - 1;
          valueIndex += counts[length];
        } else {
          ht.MinCode[length] = -1;
          ht.MaxCode[length] = -1;
        }

        code = (code + counts[length]) << 1;
      }

      tables[tableId] = ht;
    }
  }

  /// <summary>Read a 16-bit big-endian value and advance offset.</summary>
  private static ushort _ReadUInt16BE(ReadOnlySpan<byte> data, ref int offset) {
    var value = (ushort)((data[offset] << 8) | data[offset + 1]);
    offset += 2;
    return value;
  }

  /// <summary>Reassemble Canon CR2 sliced data into a single contiguous image.</summary>
  /// <param name="samples">Decoded interleaved samples from the lossless JPEG decoder.</param>
  /// <param name="width">Full image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="sliceWidths">Array of slice widths (typically 3 values: first slice width, middle count, last slice width).</param>
  /// <param name="componentCount">Number of interleaved components (typically 2 for CR2).</param>
  /// <returns>Rearranged single-channel CFA data in raster order.</returns>
  internal static ushort[] ReassembleCanonSlices(ushort[] samples, int width, int height, int[] sliceWidths, int componentCount) {
    if (sliceWidths.Length < 3)
      return _DeinterleaveToSingleChannel(samples, width, height, componentCount);

    // Canon slice info: [count1, count2, width3]
    // sliceWidths[0] = width of first slice
    // sliceWidths[1] = number of middle slices (each with width = width of image / number derived)
    // sliceWidths[2] = width of last slice
    // Actually, Canon CR2 slice info tag (0xC640) has 3 SHORT values:
    // count of "first" slices, count of "middle" slices, width of the last slice
    // Typical: [N, S, W] where there are N slices of width (full_width - W) / N, then 1 slice of width W
    // Or more commonly: [numSlice1, numSlice2, slice2Width]
    // where slices come as: numSlice1 slices of one width, numSlice2 slices of slice2Width

    var slice1Count = sliceWidths[0];
    var slice2Count = sliceWidths[1];
    var slice2Width = sliceWidths[2];

    var totalSlices = slice1Count + slice2Count;
    var slice1Width = totalSlices > 0 && slice1Count > 0
      ? (width - slice2Count * slice2Width) / slice1Count
      : width;

    // Build slice descriptor list
    var slices = new List<int>();
    for (var i = 0; i < slice1Count; ++i)
      slices.Add(slice1Width);
    for (var i = 0; i < slice2Count; ++i)
      slices.Add(slice2Width);

    // Reassemble: the decoded data is in slice-column-major order
    var result = new ushort[width * height];
    var srcIdx = 0;

    for (var s = 0; s < slices.Count; ++s) {
      var sliceW = slices[s];
      var xBase = 0;
      for (var si = 0; si < s; ++si)
        xBase += slices[si];

      for (var y = 0; y < height; ++y)
        for (var x = 0; x < sliceW; ++x) {
          var destX = xBase + x;
          if (destX < width && srcIdx < samples.Length) {
            // For multi-component, take just the first component channel per pixel
            if (componentCount > 1) {
              result[y * width + destX] = samples[srcIdx];
              srcIdx += componentCount;
            } else {
              result[y * width + destX] = samples[srcIdx++];
            }
          }
        }
    }

    return result;
  }

  /// <summary>De-interleave multi-component samples to a single CFA channel.</summary>
  private static ushort[] _DeinterleaveToSingleChannel(ushort[] samples, int width, int height, int componentCount) {
    if (componentCount <= 1)
      return samples;

    var result = new ushort[width * height];
    var srcIdx = 0;
    for (var i = 0; i < result.Length && srcIdx < samples.Length; ++i) {
      result[i] = samples[srcIdx];
      srcIdx += componentCount;
    }

    return result;
  }
}
