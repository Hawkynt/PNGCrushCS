using System;
using System.IO;

namespace FileFormat.JpegLs;

/// <summary>Reads JPEG-LS files from bytes, streams, or file paths.</summary>
public static class JpegLsReader {

  public static JpegLsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG-LS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JpegLsFile FromStream(Stream stream) {
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

  public static JpegLsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static JpegLsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid JPEG-LS file.");

    var dataArray = data.ToArray();
    var pos = 0;

    if (_ReadMarker(dataArray, ref pos) != JpegLsCodec.MarkerSoi)
      throw new InvalidDataException("Missing JPEG SOI marker (FF D8).");

    int bitsPerSample = 0, height = 0, width = 0, componentCount = 0, near = 0;
    var foundSof = false;
    byte[]? pixelData = null;

    int? customT1 = null, customT2 = null, customT3 = null, customReset = null, customMaxVal = null;

    while (pos < dataArray.Length - 1) {
      // Look for marker prefix
      if (dataArray[pos] != 0xFF) {
        ++pos;
        continue;
      }

      // Skip fill bytes (0xFF followed by 0xFF)
      while (pos < dataArray.Length - 1 && dataArray[pos + 1] == 0xFF)
        ++pos;

      if (pos >= dataArray.Length - 1)
        break;

      var marker = _ReadMarker(dataArray, ref pos);

      switch (marker) {
        case JpegLsCodec.MarkerSof55:
          _ParseSof55(dataArray, ref pos, out bitsPerSample, out height, out width, out componentCount);
          foundSof = true;
          break;

        case JpegLsCodec.MarkerLse:
          _ParseLse(dataArray, ref pos, ref customMaxVal, ref customT1, ref customT2, ref customT3, ref customReset);
          break;

        case JpegLsCodec.MarkerSos: {
          if (!foundSof)
            throw new InvalidDataException("SOS marker found before SOF55.");
          _ParseSos(dataArray, ref pos, out var componentIds, out near, out _);

          var bytesPerSample = bitsPerSample > 8 ? 2 : 1;
          pixelData ??= new byte[width * height * componentCount * bytesPerSample];

          var maxVal = customMaxVal ?? ((1 << bitsPerSample) - 1);

          foreach (var cId in componentIds) {
            var codec = _CreateCodec(maxVal, near, customT1, customT2, customT3, customReset);

            var scanEnd = _FindScanEnd(dataArray, pos);
            var stuffed = new byte[scanEnd - pos];
            data.Slice(pos, stuffed.Length).CopyTo(stuffed.AsSpan(0));
            var scanData = _ByteUnstuff(stuffed);

            var componentSamples = _DecodeSingleComponent(scanData, width, height, codec);
            _InterleaveComponent(pixelData, componentSamples, width, height, componentCount, cId - 1, bytesPerSample);

            pos = scanEnd;
          }

          break;
        }

        case JpegLsCodec.MarkerEoi:
          goto done;

        default:
          // Skip unknown marker segments
          if (pos + 1 < dataArray.Length) {
            var segLen = (dataArray[pos] << 8) | dataArray[pos + 1];
            pos += segLen;
          }
          break;
      }
    }

    done:

    if (!foundSof)
      throw new InvalidDataException("No SOF55 marker found in JPEG-LS data.");
    if (pixelData == null)
      throw new InvalidDataException("No SOS marker found in JPEG-LS data.");

    return new JpegLsFile {
      Width = width,
      Height = height,
      BitsPerSample = bitsPerSample,
      ComponentCount = componentCount,
      NearLossless = near,
      PixelData = pixelData,
    };
  }

  private static ushort _ReadMarker(byte[] data, ref int pos) {
    if (pos + 1 >= data.Length)
      throw new InvalidDataException("Unexpected end of data while reading marker.");
    var marker = (ushort)((data[pos] << 8) | data[pos + 1]);
    pos += 2;
    return marker;
  }

  private static void _ParseSof55(byte[] data, ref int pos, out int bitsPerSample, out int height, out int width, out int componentCount) {
    var length = (data[pos] << 8) | data[pos + 1];
    var start = pos;
    pos += 2;

    bitsPerSample = data[pos++];
    height = (data[pos] << 8) | data[pos + 1]; pos += 2;
    width = (data[pos] << 8) | data[pos + 1]; pos += 2;
    componentCount = data[pos++];

    pos = start + length;
  }

  private static void _ParseSos(byte[] data, ref int pos, out byte[] componentIds, out int near, out JpegLsInterleaveMode interleave) {
    var length = (data[pos] << 8) | data[pos + 1];
    var start = pos;
    pos += 2;

    var ns = data[pos++];
    componentIds = new byte[ns];
    for (var i = 0; i < ns; ++i) {
      componentIds[i] = data[pos++];
      pos++; // mapping table index
    }

    near = data[pos++];
    interleave = (JpegLsInterleaveMode)data[pos++];
    pos++; // point transform

    pos = start + length;
  }

  private static void _ParseLse(byte[] data, ref int pos, ref int? maxVal, ref int? t1, ref int? t2, ref int? t3, ref int? reset) {
    var length = (data[pos] << 8) | data[pos + 1];
    var start = pos;
    pos += 2;

    var type = data[pos++];
    if (type == JpegLsCodec.LsePresetParameters) {
      maxVal = (data[pos] << 8) | data[pos + 1]; pos += 2;
      t1 = (data[pos] << 8) | data[pos + 1]; pos += 2;
      t2 = (data[pos] << 8) | data[pos + 1]; pos += 2;
      t3 = (data[pos] << 8) | data[pos + 1]; pos += 2;
      reset = (data[pos] << 8) | data[pos + 1]; pos += 2;
    }

    pos = start + length;
  }

  private static JpegLsCodec _CreateCodec(int maxVal, int near, int? t1, int? t2, int? t3, int? reset) {
    if (t1.HasValue && t2.HasValue && t3.HasValue)
      return new(maxVal, near, t1.Value, t2.Value, t3.Value, reset ?? JpegLsCodec.DefaultReset);
    return JpegLsCodec.CreateDefault(maxVal, near);
  }

  private static int _FindScanEnd(byte[] data, int start) {
    // Scan data ends at the next 0xFF byte followed by a non-zero, non-0xFF byte
    // (0xFF 0x00 is byte-stuffing, not a marker)
    for (var i = start; i < data.Length - 1; ++i) {
      if (data[i] == 0xFF && data[i + 1] != 0x00 && data[i + 1] != 0xFF)
        return i;
      // 0xFF 0xFF is fill bytes before a marker, treat as end
      if (data[i] == 0xFF && data[i + 1] == 0xFF)
        return i;
    }
    return data.Length;
  }

  private static byte[] _ByteUnstuff(byte[] data) {
    var count = 0;
    for (var i = 0; i < data.Length - 1; ++i)
      if (data[i] == 0xFF && data[i + 1] == 0x00)
        ++count;

    if (count == 0)
      return data;

    var result = new byte[data.Length - count];
    var j = 0;
    for (var i = 0; i < data.Length; ++i) {
      result[j++] = data[i];
      if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00)
        ++i;
    }

    if (j < result.Length)
      Array.Resize(ref result, j);

    return result;
  }

  private static int[] _DecodeSingleComponent(byte[] scanData, int width, int height, JpegLsCodec codec) {
    var samples = new int[width * height];
    var reader = new BitReader(scanData);

    for (var y = 0; y < height; ++y) {
      var x = 0;
      while (x < width) {
        JpegLsPredictor.GetNeighbors(samples, width, height, x, y, out var a, out var b, out var c, out var d);
        JpegLsPredictor.ComputeGradients(a, b, c, d, out var d1, out var d2, out var d3);

        var ctx = codec.QuantizeContext(d1, d2, d3, out var negative);

        if (ctx < 0) {
          x = JpegLsRunMode.Decode(reader, samples, width, x, y, a, codec);
          continue;
        }

        var idx = y * width + x;
        samples[idx] = codec.DecodeRegular(reader, a, b, c, ctx, negative);
        ++x;
      }
    }

    return samples;
  }

  private static void _InterleaveComponent(byte[] pixelData, int[] component, int width, int height, int componentCount, int componentIndex, int bytesPerSample) {
    var size = width * height;
    if (bytesPerSample == 2)
      for (var i = 0; i < size; ++i) {
        var offset = (i * componentCount + componentIndex) * 2;
        var val = (ushort)component[i];
        pixelData[offset] = (byte)(val >> 8);
        pixelData[offset + 1] = (byte)(val & 0xFF);
      }
    else
      for (var i = 0; i < size; ++i)
        pixelData[i * componentCount + componentIndex] = (byte)component[i];
  }
}

/// <summary>Bit-level reader for Golomb-Rice codes, reading MSB-first.</summary>
internal sealed class BitReader {

  private readonly byte[] _data;
  private int _bytePos;
  private int _bitPos;

  internal BitReader(byte[] data) {
    _data = data;
    _bytePos = 0;
    _bitPos = 7;
  }

  internal int ReadBit() {
    if (_bytePos >= _data.Length)
      return 0;

    var bit = (_data[_bytePos] >> _bitPos) & 1;
    --_bitPos;
    if (_bitPos < 0) {
      _bitPos = 7;
      ++_bytePos;
    }
    return bit;
  }

  internal int ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | ReadBit();
    return value;
  }
}
