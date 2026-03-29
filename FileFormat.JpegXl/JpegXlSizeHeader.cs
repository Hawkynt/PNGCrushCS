using System;

namespace FileFormat.JpegXl;

/// <summary>
/// Encodes and decodes the JPEG XL SizeHeader using bit-level variable-length coding.
/// Per ISO/IEC 18181-1, the SizeHeader follows the 2-byte codestream signature (FF 0A).
/// </summary>
internal static class JpegXlSizeHeader {

  /// <summary>Predefined small sizes (ratio-based) when small=true (first bit=0).</summary>
  private static readonly (int Width, int Height)[] _SmallSizes = [
    (1, 1),
    (1, 1), // not actually used for index 0; index meanings per spec are aspect-ratio
    (1, 1),
    (1, 1),
    (1, 1),
    (1, 1),
    (1, 1),
    (1, 1),
  ];

  /// <summary>
  /// Decodes width and height from the codestream bytes starting after the FF 0A signature.
  /// Returns the dimensions and the number of bytes consumed.
  /// </summary>
  public static (int Width, int Height, int BytesConsumed) Decode(ReadOnlySpan<byte> data) {
    if (data.Length < 1)
      throw new InvalidOperationException("Not enough data to decode JXL size header.");

    var reader = new BitReader(data);

    // First bit: small flag
    var small = reader.ReadBits(1) == 0;

    if (small) {
      // small: 5-bit height_div8 (height = (val+1)*8), ratio from 3 bits
      var heightDiv8 = (int)reader.ReadBits(5);
      var height = (heightDiv8 + 1) * 8;
      var ratio = (int)reader.ReadBits(3);
      var width = _GetWidthFromRatio(ratio, height);
      return (width, height, reader.BytesConsumed);
    }

    // Not small: read height and width via u32 encoding
    var h = _ReadU32(ref reader);
    var ratioLarge = (int)reader.ReadBits(3);
    int w;
    if (ratioLarge == 0)
      w = _ReadU32(ref reader);
    else
      w = _GetWidthFromRatio(ratioLarge, h);

    return (w, h, reader.BytesConsumed);
  }

  /// <summary>
  /// Encodes width and height into the SizeHeader bit format.
  /// Returns the encoded bytes.
  /// </summary>
  public static byte[] Encode(int width, int height) {
    var writer = new BitWriter();

    // Try small encoding if possible
    if (height >= 8 && height <= 256 && height % 8 == 0) {
      var ratio = _FindRatio(width, height);
      if (ratio > 0) {
        writer.WriteBits(0, 1); // small=true (bit value 0)
        writer.WriteBits((uint)(height / 8 - 1), 5);
        writer.WriteBits((uint)ratio, 3);
        return writer.ToArray();
      }
    }

    // Large encoding
    writer.WriteBits(1, 1); // small=false (bit value 1)
    _WriteU32(ref writer, height);
    var ratioLarge = _FindRatio(width, height);
    writer.WriteBits((uint)ratioLarge, 3);
    if (ratioLarge == 0)
      _WriteU32(ref writer, width);

    return writer.ToArray();
  }

  /// <summary>
  /// Reads a u32 value using the JXL variable-length encoding:
  /// 2-bit selector:
  ///   0 => value = 0 (no more bits)
  ///   1 => value = 1 + 4 bits (1..16)
  ///   2 => value = 17 + 8 bits (17..272)
  ///   3 => value = 273 + 12 bits (273..4368) -- but we extend for larger with full 32-bit
  /// The actual JXL spec uses configurable distributions, but for SizeHeader:
  ///   selector 0 => val + 1 (0 extra bits, val=1)
  ///   selector 1 => val + 1 (8 extra bits)
  ///   selector 2 => val + 1 (16 extra bits)
  ///   selector 3 => val + 1 (32 extra bits)
  /// Per the spec, the SizeHeader u32 encoding for height/width is:
  ///   u32(1, 1, 1, 1, u(9), u(13), u(18), u(30))
  /// which means:
  ///   sel 0 => offset 1, 0 bits => value = 1
  ///   sel 1 => offset 1, 9 bits => value = 1..512
  ///   sel 2 => offset 1, 13 bits => value = 1..8192
  ///   sel 3 => offset 1, 18 bits => value = 1..262144
  /// </summary>
  private static int _ReadU32(ref BitReader reader) {
    var selector = (int)reader.ReadBits(2);
    return selector switch {
      0 => 1,
      1 => 1 + (int)reader.ReadBits(9),
      2 => 1 + (int)reader.ReadBits(13),
      3 => 1 + (int)reader.ReadBits(18),
      _ => throw new InvalidOperationException("Unexpected u32 selector.")
    };
  }

  private static void _WriteU32(ref BitWriter writer, int value) {
    if (value < 1)
      throw new ArgumentOutOfRangeException(nameof(value), "SizeHeader dimension must be >= 1.");

    if (value == 1) {
      writer.WriteBits(0, 2); // selector 0 => value = 1
      return;
    }

    var offset = value - 1;
    if (offset <= 511) {
      writer.WriteBits(1, 2); // selector 1 => 9 bits
      writer.WriteBits((uint)offset, 9);
    } else if (offset <= 8191) {
      writer.WriteBits(2, 2); // selector 2 => 13 bits
      writer.WriteBits((uint)offset, 13);
    } else if (offset <= 262143) {
      writer.WriteBits(3, 2); // selector 3 => 18 bits
      writer.WriteBits((uint)offset, 18);
    } else
      throw new ArgumentOutOfRangeException(nameof(value), $"SizeHeader dimension {value} too large (max 262144).");
  }

  private static int _GetWidthFromRatio(int ratio, int height) => ratio switch {
    0 => height, // placeholder; caller must read width separately
    1 => height,
    2 => (int)Math.Ceiling(height * 12.0 / 10.0),
    3 => (int)Math.Ceiling(height * 4.0 / 3.0),
    4 => (int)Math.Ceiling(height * 3.0 / 2.0),
    5 => (int)Math.Ceiling(height * 16.0 / 9.0),
    6 => (int)Math.Ceiling(height * 5.0 / 4.0),
    7 => height * 2,
    _ => throw new InvalidOperationException($"Unknown ratio {ratio}.")
  };

  private static int _FindRatio(int width, int height) {
    if (width == height)
      return 1;
    if (width == (int)Math.Ceiling(height * 12.0 / 10.0))
      return 2;
    if (width == (int)Math.Ceiling(height * 4.0 / 3.0))
      return 3;
    if (width == (int)Math.Ceiling(height * 3.0 / 2.0))
      return 4;
    if (width == (int)Math.Ceiling(height * 16.0 / 9.0))
      return 5;
    if (width == (int)Math.Ceiling(height * 5.0 / 4.0))
      return 6;
    if (width == height * 2)
      return 7;
    return 0;
  }

  /// <summary>Bit-level reader over a byte span.</summary>
  internal ref struct BitReader {

    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    public BitReader(ReadOnlySpan<byte> data) {
      _data = data;
      _bitPosition = 0;
    }

    public int BytesConsumed => (_bitPosition + 7) / 8;

    public uint ReadBits(int count) {
      if (count == 0)
        return 0;
      if (count > 32)
        throw new ArgumentOutOfRangeException(nameof(count));

      var result = 0u;
      for (var i = 0; i < count; ++i) {
        var byteIndex = _bitPosition / 8;
        var bitIndex = _bitPosition % 8;
        if (byteIndex >= _data.Length)
          throw new InvalidOperationException("Not enough data to read bits from JXL size header.");

        var bit = (_data[byteIndex] >> bitIndex) & 1;
        result |= (uint)bit << i;
        ++_bitPosition;
      }
      return result;
    }
  }

  /// <summary>Bit-level writer producing an LSB-first byte array.</summary>
  internal struct BitWriter {

    private byte[] _buffer;
    private int _bitPosition;

    public BitWriter() {
      _buffer = new byte[16];
      _bitPosition = 0;
    }

    public void WriteBits(uint value, int count) {
      for (var i = 0; i < count; ++i) {
        var byteIndex = _bitPosition / 8;
        var bitIndex = _bitPosition % 8;

        if (byteIndex >= _buffer.Length) {
          var newBuffer = new byte[_buffer.Length * 2];
          Array.Copy(_buffer, newBuffer, _buffer.Length);
          _buffer = newBuffer;
        }

        var bit = (value >> i) & 1;
        _buffer[byteIndex] |= (byte)(bit << bitIndex);
        ++_bitPosition;
      }
    }

    public readonly byte[] ToArray() {
      var byteCount = (_bitPosition + 7) / 8;
      var result = new byte[byteCount];
      Array.Copy(_buffer, result, byteCount);
      return result;
    }
  }
}
