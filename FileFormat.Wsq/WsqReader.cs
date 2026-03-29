using System;
using System.IO;

namespace FileFormat.Wsq;

/// <summary>Reads WSQ files from bytes, streams, or file paths.</summary>
public static class WsqReader {

  private const int _MIN_FILE_SIZE = 10; // SOI(2) + SOF marker(2+len) + EOI(2) minimum

  public static WsqFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WSQ file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WsqFile FromStream(Stream stream) {
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

  public static WsqFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid WSQ file.");

    var marker = WsqMarker.ReadUInt16BE(data, 0);
    if (marker != WsqMarker.SOI)
      throw new InvalidDataException($"Invalid WSQ signature: expected 0x{WsqMarker.SOI:X4}, got 0x{marker:X4}.");

    return _Parse(data);
  }

  private static WsqFile _Parse(byte[] data) {
    var pos = 2; // Skip SOI
    var width = 0;
    var height = 0;
    var ppi = 500;
    var scale = 0.0;
    var shift = 0.0;
    WsqHuffman.HuffmanTable? huffTable = null;
    WsqQuantizer.QuantParams[]? quantParams = null;
    byte[]? scanData = null;
    var numSubbands = WsqWavelet.NUM_SUBBANDS;

    while (pos < data.Length - 1) {
      var marker = WsqMarker.ReadUInt16BE(data, pos);
      pos += 2;

      switch (marker) {
        case WsqMarker.EOI:
          goto doneMarkers;

        case WsqMarker.SOF:
          _ParseSOF(data, ref pos, out width, out height, out scale, out shift, out ppi);
          break;

        case WsqMarker.SOB:
          quantParams = _ParseSOB(data, ref pos, numSubbands);
          break;

        case WsqMarker.DTT:
          _SkipSegment(data, ref pos); // Filter taps are hardcoded (CDF 9/7)
          break;

        case WsqMarker.DQT:
          _SkipSegment(data, ref pos); // Quantization info already in SOB
          break;

        case WsqMarker.DHT:
          huffTable = _ParseDHT(data, ref pos);
          break;

        case WsqMarker.SOS:
          scanData = _ParseSOS(data, ref pos);
          goto doneMarkers;

        case WsqMarker.COM:
          _SkipSegment(data, ref pos);
          break;

        default:
          // Unknown marker with segment — try to skip
          if ((marker & 0xFF00) == 0xFF00 && pos + 1 < data.Length)
            _SkipSegment(data, ref pos);
          break;
      }
    }

    doneMarkers:

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("WSQ file missing frame header (SOF).");
    if (scanData == null)
      throw new InvalidDataException("WSQ file missing scan data (SOS).");

    // Default quantization params if not present
    quantParams ??= _DefaultQuantParams(numSubbands);

    // Default Huffman table if not present
    if (huffTable == null) {
      huffTable = new WsqHuffman.HuffmanTable {
        CodeLengths = [0, 2, 1, 3, 3, 2, 4, 3, 5, 0, 0, 0, 0, 0, 0, 0],
        Values = _GenerateDefaultValues(23)
      };
    }

    // Decode
    var totalCoeffs = width * height;
    var indices = WsqHuffman.Decode(scanData, 0, totalCoeffs, huffTable);
    var coeffs = WsqQuantizer.Dequantize(indices, width, height, quantParams);

    // Apply scale and shift in reverse
    for (var i = 0; i < coeffs.Length; ++i)
      coeffs[i] = coeffs[i] + shift;

    var pixels = WsqWavelet.Inverse2D(coeffs, width, height);

    return new WsqFile {
      Width = width,
      Height = height,
      Ppi = ppi,
      PixelData = pixels
    };
  }

  private static void _ParseSOF(byte[] data, ref int pos, out int width, out int height, out double scale, out double shift, out int ppi) {
    var len = WsqMarker.ReadUInt16BE(data, pos);
    var end = pos + len;
    pos += 2;

    // Black reference value (not needed for basic decode but included in format)
    var blackRef = _ReadUInt8(data, ref pos);
    // White reference value
    var whiteRef = _ReadUInt8(data, ref pos);

    height = WsqMarker.ReadUInt16BE(data, pos);
    pos += 2;
    width = WsqMarker.ReadUInt16BE(data, pos);
    pos += 2;

    scale = 1.0;
    shift = 0.0;
    ppi = 500;

    // Read scale/shift if present
    if (pos + 8 <= end) {
      scale = BitConverter.IsLittleEndian
        ? BitConverter.ToSingle(_ReverseBytes(data, pos, 4), 0)
        : BitConverter.ToSingle(data, pos);
      pos += 4;
      shift = BitConverter.IsLittleEndian
        ? BitConverter.ToSingle(_ReverseBytes(data, pos, 4), 0)
        : BitConverter.ToSingle(data, pos);
      pos += 4;
    }

    // Read PPI if present
    if (pos + 2 <= end) {
      ppi = WsqMarker.ReadUInt16BE(data, pos);
      pos += 2;
      if (ppi == 0)
        ppi = 500;
    }

    pos = end;
  }

  private static WsqQuantizer.QuantParams[] _ParseSOB(byte[] data, ref int pos, int numSubbands) {
    var len = WsqMarker.ReadUInt16BE(data, pos);
    var end = pos + len;
    pos += 2;

    var count = _ReadUInt8(data, ref pos);
    if (count > numSubbands)
      count = numSubbands;

    var result = new WsqQuantizer.QuantParams[numSubbands];
    for (var i = 0; i < count && pos + 8 <= end; ++i) {
      var binWidth = BitConverter.IsLittleEndian
        ? BitConverter.ToSingle(_ReverseBytes(data, pos, 4), 0)
        : BitConverter.ToSingle(data, pos);
      pos += 4;
      var zeroBin = BitConverter.IsLittleEndian
        ? BitConverter.ToSingle(_ReverseBytes(data, pos, 4), 0)
        : BitConverter.ToSingle(data, pos);
      pos += 4;
      result[i] = new(binWidth, zeroBin);
    }

    // Fill remaining with defaults
    for (var i = count; i < numSubbands; ++i)
      result[i] = new(1.0, 0.44);

    pos = end;
    return result;
  }

  private static WsqHuffman.HuffmanTable _ParseDHT(byte[] data, ref int pos) {
    var len = WsqMarker.ReadUInt16BE(data, pos);
    var end = pos + len;
    pos += 2;

    // Table class and identifier (1 byte: high nibble = class, low nibble = id)
    var classId = _ReadUInt8(data, ref pos);

    var codeLengths = new byte[16];
    var totalValues = 0;
    for (var i = 0; i < 16 && pos < end; ++i) {
      codeLengths[i] = data[pos++];
      totalValues += codeLengths[i];
    }

    var values = new byte[totalValues];
    for (var i = 0; i < totalValues && pos < end; ++i)
      values[i] = data[pos++];

    pos = end;
    return new WsqHuffman.HuffmanTable { CodeLengths = codeLengths, Values = values };
  }

  private static byte[] _ParseSOS(byte[] data, ref int pos) {
    var len = WsqMarker.ReadUInt16BE(data, pos);
    var headerEnd = pos + len;
    pos += 2;

    // Read component count and table selection
    var compCount = _ReadUInt8(data, ref pos);
    var tableSel = _ReadUInt8(data, ref pos);

    // Read scan data length (4-byte big-endian)
    var scanLen = 0;
    if (pos + 4 <= headerEnd) {
      scanLen = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
      pos += 4;
    }

    pos = headerEnd; // Skip to end of SOS header

    // Extract scan data
    if (scanLen > 0 && pos + scanLen <= data.Length) {
      var scanData = new byte[scanLen];
      data.AsSpan(pos, scanLen).CopyTo(scanData.AsSpan(0));
      pos += scanLen;
      return scanData;
    }

    // Fallback: read until EOI or end of data
    var start = pos;
    var end = data.Length;
    for (var i = pos; i < data.Length - 1; ++i) {
      if (data[i] == 0xFF && data[i + 1] >= 0xA0 && data[i + 1] != 0x00) {
        end = i;
        break;
      }
    }

    var fallbackData = new byte[end - start];
    data.AsSpan(start, fallbackData.Length).CopyTo(fallbackData.AsSpan(0));
    pos = end;
    return fallbackData;
  }

  private static void _SkipSegment(byte[] data, ref int pos) {
    if (pos + 1 >= data.Length)
      return;
    var len = WsqMarker.ReadUInt16BE(data, pos);
    pos += len;
  }

  private static int _ReadUInt8(byte[] data, ref int pos) => data[pos++];

  private static byte[] _ReverseBytes(byte[] data, int offset, int count) {
    var result = new byte[count];
    for (var i = 0; i < count; ++i)
      result[i] = data[offset + count - 1 - i];
    return result;
  }

  private static WsqQuantizer.QuantParams[] _DefaultQuantParams(int count) {
    var result = new WsqQuantizer.QuantParams[count];
    for (var i = 0; i < count; ++i)
      result[i] = new(1.0, 0.44);
    return result;
  }

  private static byte[] _GenerateDefaultValues(int count) {
    var values = new byte[count];
    for (var i = 0; i < count; ++i)
      values[i] = (byte)i;
    return values;
  }
}
