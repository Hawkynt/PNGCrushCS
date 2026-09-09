using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using FileFormat.Core;

namespace FileFormat.Crw;

/// <summary>Writes Canon CIFF raw files using Canon's three fixed Huffman table pairs.</summary>
/// <remarks>
/// The raw-data record is deliberately first. Existing Canon decoders address its payload as file
/// offsets 26 and 540 rather than through the CIFF directory, so moving it would produce a formally
/// navigable heap that those decoders cannot read.
/// </remarks>
public static class CrwWriter {

  private const int _HEADER_SIZE = 26;
  private const int _COMPRESSED_DATA_GAP = 514;
  private const ushort _RECORD_SENSOR_INFO = 0x1031;
  private const ushort _RECORD_IMAGE_SPEC = 0x1810;
  private const ushort _RECORD_DECODER_TABLE = 0x1835;
  private const ushort _RECORD_SENSOR_DATA = 0x2005;

  private readonly record struct _Record(ushort Type, byte[] Content);
  private readonly record struct _Code(int Bits, int Length);

  /// <summary>An encoding view built by inverting the reader's own flattened Huffman table.</summary>
  private sealed class _EncoderTable {
    private readonly _Code[] _codes = new _Code[256];

    public _EncoderTable(CrwHuffman.Table decoder) {
      for (var index = 0; index < decoder.Entries.Length; ++index) {
        var entry = decoder.Entries[index];
        var length = entry >> 8;
        var symbol = entry & 0xFF;
        if (length == 0 || this._codes[symbol].Length != 0)
          continue;

        this._codes[symbol] = new(index >> (decoder.Bits - length), length);
      }
    }

    public _Code this[int symbol] {
      get {
        var result = this._codes[symbol];
        if (result.Length == 0)
          throw new InvalidDataException($"Canon Huffman table contains no symbol 0x{symbol:X2}.");

        return result;
      }
    }
  }

  /// <summary>Big-endian bit writer with Canon/JPEG 0x00 stuffing after every 0xFF byte.</summary>
  private sealed class _BitWriter {
    private readonly List<byte> _bytes = [];
    private uint _buffer;
    private int _available;

    public void Write(_Code code) => this.Write(code.Bits, code.Length);

    public void Write(int value, int count) {
      if (count <= 0)
        return;

      var mask = (1u << count) - 1;
      this._buffer = this._buffer << count | ((uint)value & mask);
      this._available += count;

      while (this._available >= 8) {
        var shift = this._available - 8;
        this._Emit((byte)(this._buffer >> shift));
        this._available -= 8;
        this._buffer = this._available == 0
          ? 0
          : this._buffer & ((1u << this._available) - 1);
      }
    }

    public byte[] Finish() {
      if (this._available > 0)
        this._Emit((byte)(this._buffer << (8 - this._available)));

      this._buffer = 0;
      this._available = 0;
      return [.. this._bytes];
    }

    private void _Emit(byte value) {
      this._bytes.Add(value);
      if (value == 0xFF)
        this._bytes.Add(0);
    }
  }

  /// <summary>Serializes a CRW image into a little-endian CIFF 1.2 heap.</summary>
  public static byte[] ToBytes(CrwFile file) {
    _Validate(file);

    var bitsPerSample = _BitsPerSample(file);
    var lowBits = bitsPerSample == 12;
    const int tableNumber = 0;

    var compressed = _Encode(file.Sensor, file.SensorWidth, bitsPerSample, tableNumber);
    var sensorData = _SensorData(file.Sensor, file.SensorWidth, lowBits, compressed);
    var sensorInfo = _SensorInfo(file);
    var imageSpec = _ImageSpec(file);
    var decoderTable = _DecoderTable(tableNumber, compressed.Length);

    _Record[] records = [
      new(_RECORD_SENSOR_DATA, sensorData),
      new(_RECORD_SENSOR_INFO, sensorInfo),
      new(_RECORD_IMAGE_SPEC, imageSpec),
      new(_RECORD_DECODER_TABLE, decoderTable),
    ];

    var payloadLength = 0;
    foreach (var record in records)
      payloadLength = checked(payloadLength + record.Content.Length);

    var tableOffset = payloadLength;
    var heapLength = checked(payloadLength + 2 + records.Length * 10 + 4);
    var result = new byte[checked(_HEADER_SIZE + heapLength)];

    result[0] = (byte)'I';
    result[1] = (byte)'I';
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2), _HEADER_SIZE);
    "HEAPCCDR"u8.CopyTo(result.AsSpan(6));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), 0x00010002);

    var heap = result.AsSpan(_HEADER_SIZE);
    var offsets = new int[records.Length];
    var position = 0;
    for (var i = 0; i < records.Length; ++i) {
      offsets[i] = position;
      records[i].Content.CopyTo(heap[position..]);
      position += records[i].Content.Length;
    }

    BinaryPrimitives.WriteUInt16LittleEndian(heap[tableOffset..], (ushort)records.Length);
    position = tableOffset + 2;
    for (var i = 0; i < records.Length; ++i) {
      BinaryPrimitives.WriteUInt16LittleEndian(heap[position..], records[i].Type);
      BinaryPrimitives.WriteUInt32LittleEndian(heap[(position + 2)..], (uint)records[i].Content.Length);
      BinaryPrimitives.WriteUInt32LittleEndian(heap[(position + 6)..], (uint)offsets[i]);
      position += 10;
    }

    BinaryPrimitives.WriteUInt32LittleEndian(heap[position..], (uint)tableOffset);
    return result;
  }

  /// <summary>Builds a CRW sensor from an RGB image using the format's native RGGB mosaic.</summary>
  /// <remarks>
  /// <see cref="RawImage"/> is eight bit, so ten sensor bits already preserve every source level.
  /// The sensor width is padded to a multiple of sixty-four; CIFF's compression codes complete
  /// sixty-four-sample blocks and the active-image borders keep that padding out of the picture.
  /// </remarks>
  public static CrwFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(image), "A Canon raw picture must have positive dimensions.");

    var sensorWidth = checked((image.Width + 63) / 64 * 64);
    if (sensorWidth > short.MaxValue || image.Height > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image), "Canon CIFF sensor dimensions are signed 16-bit values.");

    var sensor = new ushort[checked(sensorWidth * image.Height)];
    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < image.Width; ++x) {
      var pixel = (y * image.Width + x) * 3;
      var component = (x & 1, y & 1) switch {
        (0, 0) => image.PixelData[pixel],
        (1, 1) => image.PixelData[pixel + 2],
        _ => image.PixelData[pixel + 1],
      };
      sensor[y * sensorWidth + x] = (ushort)((component * 1023 + 127) / 255);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Sensor = sensor,
      SensorWidth = sensorWidth,
      SensorHeight = image.Height,
      ImageLeft = 0,
      ImageTop = 0,
      BitsPerSample = 10,
    };
  }

  private static void _Validate(CrwFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new InvalidDataException("Canon CIFF picture dimensions must be positive.");
    if (file.SensorWidth <= 0 || file.SensorHeight <= 0 || file.SensorWidth > short.MaxValue || file.SensorHeight > short.MaxValue)
      throw new InvalidDataException("Canon CIFF sensor dimensions must be positive signed 16-bit values.");
    if (file.ImageLeft < 0 || file.ImageTop < 0
        || (long)file.ImageLeft + file.Width > file.SensorWidth
        || (long)file.ImageTop + file.Height > file.SensorHeight)
      throw new InvalidDataException("Canon CIFF picture borders must lie inside the sensor.");

    var sampleCount = checked(file.SensorWidth * file.SensorHeight);
    if (file.Sensor == null || file.Sensor.Length != sampleCount)
      throw new InvalidDataException($"Canon CIFF sensor requires exactly {sampleCount} samples.");

    // Eight complete rows are always block aligned when the width is a multiple of eight. The
    // final partial stripe must end on a complete sixty-four-sample block as well.
    if ((file.SensorWidth & 7) != 0 || (sampleCount & 63) != 0)
      throw new InvalidDataException("Canon CRW compression requires complete 64-sample blocks.");

    var bitsPerSample = _BitsPerSample(file);
    var maximum = bitsPerSample == 12 ? 4095 : 1023;
    foreach (var sample in file.Sensor) {
      if (sample > maximum)
        throw new InvalidDataException($"A {bitsPerSample}-bit Canon sensor sample cannot exceed {maximum}.");
      if (bitsPerSample == 12 && file.SensorWidth == 2672 && sample < 2)
        throw new InvalidDataException("The 2672-pixel Canon sensor cannot represent corrected 12-bit samples below two.");
    }
  }

  private static int _BitsPerSample(CrwFile file) {
    if (file.BitsPerSample is 10 or 12)
      return file.BitsPerSample;
    if (file.BitsPerSample != 0)
      throw new InvalidDataException("Canon CRW sensor precision must be 10 or 12 bits.");

    foreach (var sample in file.Sensor)
      if (sample > 1023)
        return 12;

    return 10;
  }

  private static byte[] _SensorInfo(CrwFile file) {
    var right = checked(file.ImageLeft + file.Width - 1);
    var bottom = checked(file.ImageTop + file.Height - 1);
    if (right > short.MaxValue || bottom > short.MaxValue)
      throw new InvalidDataException("Canon CIFF picture borders are signed 16-bit values.");

    var result = new byte[18];
    _WriteShort(result, 0, result.Length);
    _WriteShort(result, 1, file.SensorWidth);
    _WriteShort(result, 2, file.SensorHeight);
    _WriteShort(result, 3, 1);
    _WriteShort(result, 4, 1);
    _WriteShort(result, 5, file.ImageLeft);
    _WriteShort(result, 6, file.ImageTop);
    _WriteShort(result, 7, right);
    _WriteShort(result, 8, bottom);
    return result;
  }

  private static void _WriteShort(byte[] target, int index, int value)
    => BinaryPrimitives.WriteInt16LittleEndian(target.AsSpan(index * 2), checked((short)value));

  private static byte[] _ImageSpec(CrwFile file) {
    var result = new byte[28];
    BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)file.Width);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)file.Height);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), BitConverter.SingleToInt32Bits(1.0f));
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(20), 24);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), 257);
    return result;
  }

  private static byte[] _DecoderTable(int tableNumber, int compressedLength) {
    var result = new byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)tableNumber);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), _COMPRESSED_DATA_GAP);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), (uint)compressedLength);
    return result;
  }

  private static byte[] _SensorData(ushort[] sensor, int width, bool lowBits, byte[] compressed) {
    var lowPlaneLength = lowBits ? sensor.Length / 4 : 0;
    var result = new byte[checked(lowPlaneLength + _COMPRESSED_DATA_GAP + compressed.Length)];

    if (lowBits)
      for (var i = 0; i < sensor.Length; i += 4) {
        var a = _StoredSample(sensor[i], width, true);
        var b = _StoredSample(sensor[i + 1], width, true);
        var c = _StoredSample(sensor[i + 2], width, true);
        var d = _StoredSample(sensor[i + 3], width, true);
        result[i >> 2] = (byte)(
          (a & 3)
          | (b & 3) << 2
          | (c & 3) << 4
          | (d & 3) << 6
        );
      }

    // Canon's old readers infer the presence of the low-bit plane by finding an unstuffed 0xFF in
    // the first 16 KiB. When the otherwise-unused 514-byte gap is inside that window, give the
    // heuristic an explicit harmless witness rather than depending on the photograph's low bits.
    if (lowBits) {
      var marker = Math.Max(_COMPRESSED_DATA_GAP, lowPlaneLength);
      if (marker + 1 < lowPlaneLength + _COMPRESSED_DATA_GAP) {
        result[marker] = 0xFF;
        result[marker + 1] = 0x01;
      }
    }

    compressed.CopyTo(result.AsSpan(lowPlaneLength + _COMPRESSED_DATA_GAP));
    return result;
  }

  private static byte[] _Encode(ReadOnlySpan<ushort> sensor, int width, int bitsPerSample, int tableNumber) {
    var (decodedFirst, decodedSecond) = CrwHuffman.Tables(tableNumber);
    var first = new _EncoderTable(decodedFirst);
    var second = new _EncoderTable(decodedSecond);
    var writer = new _BitWriter();
    var previous = new int[2];
    Span<int> differences = stackalloc int[64];
    var carry = 0;
    var column = 0;
    var shift = bitsPerSample - 10;
    var lowBits = bitsPerSample == 12;

    for (var blockStart = 0; blockStart < sensor.Length; blockStart += 64) {
      for (var i = 0; i < 64; ++i) {
        if (column == 0)
          previous[0] = previous[1] = 512;

        var value = _StoredSample(sensor[blockStart + i], width, lowBits) >> shift;
        var channel = i & 1;
        differences[i] = value - previous[channel];
        previous[channel] = value;
        column = column + 1 == width ? 0 : column + 1;
      }

      var firstDifference = differences[0] - carry;
      carry = differences[0];
      _WriteDifference(writer, first, 0, firstDifference);

      var at = 1;
      while (at < 64) {
        if (differences[at] == 0) {
          var run = 1;
          while (at + run < 64 && differences[at + run] == 0)
            ++run;

          if (at + run == 64) {
            writer.Write(second[0x00]);
            break;
          }

          while (run >= 16) {
            writer.Write(second[0xF0]);
            at += 16;
            run -= 16;
          }

          _WriteDifference(writer, second, run, differences[at + run]);
          at += run + 1;
          continue;
        }

        _WriteDifference(writer, second, 0, differences[at]);
        ++at;
      }
    }

    return writer.Finish();
  }

  private static ushort _StoredSample(ushort sample, int width, bool lowBits)
    => lowBits && width == 2672 && sample < 514
      ? (ushort)(sample - 2)
      : sample;

  private static void _WriteDifference(_BitWriter writer, _EncoderTable table, int zeroRun, int difference) {
    var magnitude = Math.Abs(difference);
    var length = magnitude == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)magnitude);
    if (length > 15)
      throw new InvalidDataException("Canon CRW difference exceeds the Huffman symbol width.");

    var symbol = zeroRun << 4 | length;
    writer.Write(table[symbol]);
    if (length == 0)
      return;

    var encoded = difference > 0
      ? difference
      : difference + (1 << length) - 1;
    writer.Write(encoded, length);
  }
}
