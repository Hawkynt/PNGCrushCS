using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.CameraRaw;

namespace FileFormat.Crw;

/// <summary>Reads Canon CIFF raw files from bytes, streams, or file paths.</summary>
public static class CrwReader {

  /// <summary>The heap's own header: byte order, the length of that header, and the heap's name.</summary>
  private const int _MIN_SIZE = 26;

  /// <summary>What separates the sensor record's base from the compressed data that follows it.</summary>
  /// <remarks>
  /// The record opens with the plane holding the low two bits of every sample, on the bodies that
  /// have one, and the Huffman-coded stream begins 514 bytes past whichever of the two comes first.
  /// dcraw writes those two places as the absolute 26 and 540, which is right only because the
  /// sensor record is the first thing in the heap of every file anyone has; taking them from the
  /// record's own base is the same arithmetic without the assumption.
  /// </remarks>
  private const int _COMPRESSED_DATA_GAP = 514;

  private const ushort _RECORD_SENSOR_INFO = 0x1031;
  private const ushort _RECORD_IMAGE_SPEC = 0x1810;
  private const ushort _RECORD_DECODER_TABLE = 0x1835;
  private const ushort _RECORD_SENSOR_DATA = 0x2005;
  private const ushort _RECORD_SHOT_INFO = 0x102a;
  private const ushort _RECORD_WHITE_BALANCE_OLD = 0x102c;
  private const ushort _RECORD_WHITE_BALANCE = 0x10a9;

  /// <summary>The largest sensor this will build a picture from, which is far beyond any CIFF body.</summary>
  private const int _LARGEST_SIDE = 20000;

  public static CrwFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Canon raw file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CrwFile FromStream(Stream stream) {
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

  public static CrwFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CrwFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException("Data too small for a Canon CIFF file.");

    if (data[0] != 'I' || data[1] != 'I')
      throw new InvalidDataException("Only the little-endian Canon CIFF layout is written by any camera.");
    if (!data.AsSpan(6, 8).SequenceEqual("HEAPCCDR"u8))
      throw new InvalidDataException("Not a Canon CIFF file: the heap is not named HEAPCCDR.");

    var heapStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2));
    if (heapStart < 8 || heapStart >= data.Length)
      throw new InvalidDataException("Canon CIFF header states a heap that is not in the file.");

    var records = new List<(ushort Type, int Length, int Offset)>();
    _ReadHeap(data, heapStart, data.Length - heapStart, 0, records);

    var sensorInfo = _Record(records, _RECORD_SENSOR_INFO);
    var sensorData = _Record(records, _RECORD_SENSOR_DATA);
    if (sensorInfo.Length < 18 || sensorData.Length <= 0)
      throw new InvalidDataException("Canon CIFF file states no sensor.");

    var sensorWidth = _Short(data, sensorInfo.Offset + 2);
    var sensorHeight = _Short(data, sensorInfo.Offset + 4);
    var left = _Short(data, sensorInfo.Offset + 10);
    var top = _Short(data, sensorInfo.Offset + 12);
    var right = _Short(data, sensorInfo.Offset + 14);
    var bottom = _Short(data, sensorInfo.Offset + 16);

    if (sensorWidth <= 0 || sensorHeight <= 0 || sensorWidth > _LARGEST_SIDE || sensorHeight > _LARGEST_SIDE)
      throw new InvalidDataException($"Canon CIFF file states a sensor of {sensorWidth}x{sensorHeight}.");

    var width = right - left + 1;
    var height = bottom - top + 1;
    if (left < 0 || top < 0 || width <= 0 || height <= 0 || right >= sensorWidth || bottom >= sensorHeight)
      throw new InvalidDataException("Canon CIFF file states a picture that is not inside its sensor.");

    // The camera states the size it means to produce as well, in a record of its own. The two are
    // read separately and must agree; where they do not, one of the two was misread.
    var imageSpec = _Record(records, _RECORD_IMAGE_SPEC);
    if (imageSpec.Length >= 8) {
      var statedWidth = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(imageSpec.Offset));
      var statedHeight = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(imageSpec.Offset + 4));
      if (statedWidth != width || statedHeight != height)
        throw new InvalidDataException($"Canon CIFF file states a picture of {statedWidth}x{statedHeight} and borders describing {width}x{height}.");
    }

    var decoderTable = _Record(records, _RECORD_DECODER_TABLE);
    var table = decoderTable.Length >= 4 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(decoderTable.Offset)) : 0;

    var lowBits = _HasLowBits(data, sensorData.Offset);
    var sensor = _Decode(data, sensorData.Offset, sensorWidth, sensorHeight, table, lowBits);

    var pattern = _PatternAt(left, top);
    var black = _BlackLevel(data, sensorInfo, sensor, sensorWidth, sensorHeight);
    var white = lowBits ? 4095 : 1023;

    var cropped = new ushort[width * height];
    for (var y = 0; y < height; ++y)
      Array.Copy(sensor, (top + y) * sensorWidth + left, cropped, y * width, width);

    var balance = _WhiteBalance(data, records);
    var linear = RawPreprocessor.Process(cropped, width, height, pattern, [black], white, balance);
    var rgb = BayerDemosaic.Ahd(linear, width, height, pattern);
    RawPostprocessor.Process(rgb, null);

    return new() {
      Width = width,
      Height = height,
      PixelData = rgb,
      Sensor = sensor,
      SensorWidth = sensorWidth,
      SensorHeight = sensorHeight,
    };
  }

  private static (ushort Type, int Length, int Offset) _Record(List<(ushort Type, int Length, int Offset)> records, ushort type)
    => records.Find(record => record.Type == type);

  private static int _Short(byte[] data, int at) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at));

  /// <summary>Walks one heap: its last four bytes point at the table of what it holds.</summary>
  private static void _ReadHeap(byte[] data, int offset, int length, int depth, List<(ushort Type, int Length, int Offset)> records) {
    if (depth > 8 || length < 6 || offset < 0 || offset + length > data.Length)
      return;

    var tableOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + length - 4)) + offset;
    if (tableOffset < offset || tableOffset + 2 > data.Length)
      return;

    var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(tableOffset));
    if (count > 127)
      return;

    var at = tableOffset + 2;
    for (var i = 0; i < count && at + 10 <= data.Length; ++i, at += 10) {
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
      var recordLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 2));
      var recordOffset = offset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 6));
      if (recordLength < 0 || recordOffset < 0 || (long)recordOffset + recordLength > data.Length)
        continue;

      // A record whose type says it is a heap of its own is walked before it is recorded, so an
      // inner record of the same type as an outer one does not hide behind it.
      if ((((type >> 8) + 8) | 8) == 0x38)
        _ReadHeap(data, recordOffset, recordLength, depth + 1, records);

      records.Add((type, recordLength, recordOffset));
    }
  }

  /// <summary>Whether the record opens with the plane holding the low two bits of every sample.</summary>
  /// <remarks>
  /// Nothing states it. What tells the two apart is that the Huffman stream writes a zero after
  /// every 0xFF and a plane of raw bits does not, so a 0xFF followed by anything else, anywhere in
  /// the first pages past where the stream would start, says those pages are not the stream.
  /// </remarks>
  private static bool _HasLowBits(byte[] data, int recordOffset) {
    var end = Math.Min(data.Length - 1, 0x4000);
    var result = true;
    for (var i = recordOffset + _COMPRESSED_DATA_GAP; i < end; ++i)
      if (data[i] == 0xFF) {
        if (data[i + 1] != 0)
          return true;

        result = false;
      }

    return result;
  }

  /// <summary>Reads the sensor, one sample a pixel.</summary>
  /// <remarks>
  /// The samples are coded in blocks of sixty-four the way JPEG codes a block of coefficients: a
  /// leaf gives how many zeroes to skip and how many bits the next difference is written in, and
  /// the differences accumulate against the value two places back, so that each of the two colours
  /// on a row runs on its own. The first difference of a block carries over from the block before.
  /// </remarks>
  private static ushort[] _Decode(byte[] data, int recordOffset, int width, int height, int table, bool lowBits) {
    var (first, second) = CrwHuffman.Tables(table);
    var lowPlaneLength = lowBits ? height * width / 4 : 0;
    var bits = new CrwHuffman.BitReader(data, recordOffset + _COMPRESSED_DATA_GAP + lowPlaneLength);

    var pixels = new ushort[width * height];
    var differences = new int[64];
    var carry = 0;
    var column = 0;
    var previous = new int[2];

    for (var row = 0; row < height; row += 8) {
      var rowStart = row * width;
      var blocks = Math.Min(8, height - row) * width >> 6;
      for (var block = 0; block < blocks; ++block) {
        Array.Clear(differences);
        for (var i = 0; i < 64; ++i) {
          var leaf = bits.Read(i > 0 ? second : first);
          if (leaf == 0 && i > 0)
            break;
          if (leaf == 0xFF)
            continue;

          i += leaf >> 4;
          var length = leaf & 15;
          if (length == 0)
            continue;

          var difference = bits.Read(length);
          if ((difference & (1 << (length - 1))) == 0)
            difference -= (1 << length) - 1;
          if (i < 64)
            differences[i] = difference;
        }

        differences[0] += carry;
        carry = differences[0];
        for (var i = 0; i < 64; ++i) {
          if (column == 0)
            previous[0] = previous[1] = 512;

          column = column + 1 == width ? 0 : column + 1;
          previous[i & 1] += differences[i];
          var at = rowStart + (block << 6) + i;
          if (at < pixels.Length)
            pixels[at] = (ushort)Math.Clamp(previous[i & 1], 0, 1023);
        }
      }
    }

    if (!lowBits)
      return pixels;

    for (var row = 0; row < height; ++row) {
      var at = recordOffset + row * width / 4;
      var target = row * width;
      for (var i = 0; i < width / 4; ++i) {
        if (at + i >= data.Length)
          break;

        var packed = data[at + i];
        for (var r = 0; r < 8; r += 2, ++target) {
          if (target >= pixels.Length)
            break;

          var value = (pixels[target] << 2) + ((packed >> r) & 3);

          // One body writes its dark end two levels low; dcraw corrects it by sensor width, which
          // is the only thing that identifies it in a CIFF file.
          if (width == 2672 && value < 512)
            value += 2;

          pixels[target] = (ushort)value;
        }
      }
    }

    return pixels;
  }

  /// <summary>The colour filter over the picture's first pixel, given where the picture starts.</summary>
  /// <remarks>
  /// Every Canon CIFF sensor is red-green over green-blue at its own origin. The picture starts
  /// somewhere inside it, and an odd border in either direction moves the pattern by one.
  /// </remarks>
  private static BayerPattern _PatternAt(int left, int top) => (left & 1, top & 1) switch {
    (0, 0) => BayerPattern.RGGB,
    (1, 0) => BayerPattern.GRBG,
    (0, 1) => BayerPattern.GBRG,
    _ => BayerPattern.BGGR,
  };

  /// <summary>Averages the masked strip the file points at, which is the sensor's own zero.</summary>
  private static int _BlackLevel(byte[] data, (ushort Type, int Length, int Offset) sensorInfo, ushort[] sensor, int width, int height) {
    if (sensorInfo.Length < 26)
      return 0;

    var left = _Short(data, sensorInfo.Offset + 18);
    var top = _Short(data, sensorInfo.Offset + 20);
    var right = _Short(data, sensorInfo.Offset + 22);
    var bottom = _Short(data, sensorInfo.Offset + 24);
    if (left < 0 || top < 0 || right >= width || bottom >= height || right < left || bottom < top)
      return 0;

    long total = 0;
    long count = 0;
    for (var y = top; y <= bottom; ++y)
    for (var x = left; x <= right; ++x) {
      total += sensor[y * width + x];
      ++count;
    }

    return count == 0 ? 0 : (int)(total / count);
  }

  /// <summary>The multipliers the camera recorded for the light it was shot under.</summary>
  private static float[]? _WhiteBalance(byte[] data, List<(ushort Type, int Length, int Offset)> records) {
    var levels = _WhiteBalanceLevels(data, records);
    if (levels == null)
      return null;

    var green = (levels[1] + levels[3]) / 2.0f;
    if (green <= 0 || levels[0] <= 0 || levels[2] <= 0)
      return null;

    return [levels[0] / green, 1.0f, levels[2] / green];
  }

  /// <summary>Reads the four sensor levels, in red, green, blue, second-green order.</summary>
  private static float[]? _WhiteBalanceLevels(byte[] data, List<(ushort Type, int Length, int Offset)> records) {
    var modern = _Record(records, _RECORD_WHITE_BALANCE);
    if (modern.Length >= 10) {
      var index = 0;
      var shot = _Record(records, _RECORD_SHOT_INFO);
      if (shot.Length >= 16)
        index = _Short(data, shot.Offset + 14);

      if (modern.Length > 66) {
        // The bodies with a longer record hold their settings in a different order.
        const string ORDER = "0134567028";
        index = (uint)index < ORDER.Length ? ORDER[index] - '0' : 0;
      }

      var at = modern.Offset + 2 + index * 8;
      if (index >= 0 && at + 8 <= modern.Offset + modern.Length) {
        // The four values are stored red, green, second-green, blue.
        var red = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        var greenOne = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 2));
        var greenTwo = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4));
        var blue = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 6));
        return [red, greenOne, blue, greenTwo];
      }
    }

    var older = _Record(records, _RECORD_WHITE_BALANCE_OLD);
    if (older.Length >= 128) {
      var wide = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(older.Offset)) > 512;
      var at = older.Offset + (wide ? 120 : 100);
      if (at + 8 > older.Offset + older.Length)
        return null;

      var values = new float[4];
      for (var c = 0; c < 4; ++c)
        values[wide ? c ^ 2 : c ^ (c >> 1) ^ 1] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + c * 2));

      return values;
    }

    return null;
  }
}
