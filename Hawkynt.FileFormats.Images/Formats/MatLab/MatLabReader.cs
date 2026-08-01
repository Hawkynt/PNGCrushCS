using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MatLab;

/// <summary>Reads MATLAB Level 5 files from bytes, streams, or file paths.</summary>
/// <remarks>
/// A MAT file is a tagged container, not a picture with a header: 128 bytes of free text, then a
/// chain of elements each announcing its own type and length, one of which is an array. The array
/// in turn is a chain — its flags, then its shape, then its name, then its values.
/// <para/>
/// Nothing about the picture can be found at a fixed offset, which is what the previous reader
/// tried: it took a width and height from the first bytes of the description and answered 16717 by
/// 16961, those being two words of "MATLAB 5.0 MAT-file" read as numbers.
/// </remarks>
public static class MatLabReader {

  /// <summary>An array, which is the only element kind holding a picture.</summary>
  private const int _Matrix = 14;

  /// <summary>Unsigned bytes, which is how a picture's samples are stored.</summary>
  private const int _UInt8 = 2;

  /// <summary>Signed bytes, accepted because the values are the same eight bits.</summary>
  private const int _Int8 = 1;

  /// <summary>Signed 32-bit words, which is how a shape is stored.</summary>
  private const int _Int32 = 5;

  public static MatLabFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MATLAB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MatLabFile FromStream(Stream stream) {
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

  public static MatLabFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MatLabFile.HeaderSize + 8)
      throw new InvalidDataException("Data too small for a valid MATLAB file.");

    // The last two bytes of the header say which way round the numbers are, by spelling a word that
    // reads differently in each order.
    var isBigEndian = data[126] == 'M' && data[127] == 'I';
    if (!isBigEndian && (data[126] != 'I' || data[127] != 'M'))
      throw new InvalidDataException("Not a MATLAB Level 5 file: no byte-order mark.");

    for (var at = MatLabFile.HeaderSize; at + 8 <= data.Length;) {
      var (type, size, body, next) = _ReadTag(data, at, isBigEndian);
      if (size < 0 || body + size > data.Length || next <= at)
        break;

      if (type == _Matrix)
        return _ReadArray(data.Slice(body, size), isBigEndian);

      at = next;
    }

    throw new InvalidDataException("A MATLAB file holding no array holds no picture.");
  }

  /// <summary>Reads one element's tag, which comes in a long form and a short one.</summary>
  /// <remarks>
  /// An element of four bytes or fewer packs its length into the top half of the same word as its
  /// type, and puts the value in the four bytes that follow rather than the eight. Missing that
  /// reads the length as a type and walks off into the middle of the file.
  /// </remarks>
  private static (int Type, int Size, int Body, int Next) _ReadTag(ReadOnlySpan<byte> data, int at, bool isBigEndian) {
    var first = _ReadUInt32(data, at, isBigEndian);
    var packedSize = (int)(first >> 16);

    // The short form is always eight bytes all told, however few of them carry the value; the long
    // form is eight bytes of tag and then the value padded out to a multiple of eight.
    if (packedSize != 0)
      return ((int)(first & 0xFFFF), packedSize, at + 4, at + 8);

    var size = (int)_ReadUInt32(data, at + 4, isBigEndian);

    return ((int)first, size, at + 8, at + 8 + (size + 7) / 8 * 8);
  }

  /// <summary>Reads the chain an array is made of, and the picture at the end of it.</summary>
  private static MatLabFile _ReadArray(ReadOnlySpan<byte> array, bool isBigEndian) {
    int[]? shape = null;

    for (var at = 0; at + 8 <= array.Length;) {
      var (type, size, body, next) = _ReadTag(array, at, isBigEndian);
      if (size < 0 || body + size > array.Length || next <= at)
        break;

      if (type == _Int32 && shape == null) {
        shape = new int[size / 4];
        for (var i = 0; i < shape.Length; ++i)
          shape[i] = (int)_ReadUInt32(array, body + i * 4, isBigEndian);
      } else if (type is _UInt8 or _Int8 && shape != null && size == _Volume(shape)) {
        // The array's name is stored as signed bytes too, and comes before the values. Telling them
        // apart by type alone takes the name for the picture; the length is what distinguishes them.
        return _Build(shape, array.Slice(body, size));
      }

      at = next;
    }

    throw new InvalidDataException("A MATLAB array without a shape and eight-bit samples is not a picture.");
  }

  /// <summary>How many values a shape holds.</summary>
  private static int _Volume(int[] shape) {
    var total = 1;
    foreach (var extent in shape)
      total *= extent;

    return total;
  }

  /// <summary>Turns the array's values into a picture.</summary>
  /// <remarks>
  /// MATLAB counts down a column before moving across, and names its shape rows first — so what it
  /// calls the first dimension is the height and the second is the width, and a third is the colour
  /// planes stored one whole plane at a time.
  /// </remarks>
  private static MatLabFile _Build(int[] shape, ReadOnlySpan<byte> values) {
    if (shape.Length is < 2 or > 3)
      throw new InvalidDataException($"A MATLAB array of {shape.Length} dimensions is not a picture.");

    int height = shape[0], width = shape[1];
    var planes = shape.Length == 3 ? shape[2] : 1;
    if (width <= 0 || height <= 0 || planes is not (1 or 3))
      throw new InvalidDataException($"A MATLAB array of {width}x{height}x{planes} is not a picture.");

    var pixels = new byte[width * height * 3];
    var plane = width * height;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
    for (var channel = 0; channel < 3; ++channel) {
      var source = (planes == 1 ? 0 : channel) * plane + x * height + y;
      pixels[(y * width + x) * 3 + channel] = source < values.Length ? values[source] : (byte)0;
    }

    return new() { Width = width, Height = height, PixelData = pixels };
  }

  private static uint _ReadUInt32(ReadOnlySpan<byte> data, int at, bool isBigEndian)
    => at + 4 > data.Length
      ? 0
      : isBigEndian
        ? BinaryPrimitives.ReadUInt32BigEndian(data[at..])
        : BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);

  public static MatLabFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
