using System;
using System.IO;

namespace FileFormat.Hp48Grob;

/// <summary>Reads HP 48 graphics objects from bytes, streams, or file paths.</summary>
public static class Hp48GrobReader {

  public static Hp48GrobFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graphics object not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Hp48GrobFile FromStream(Stream stream) {
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

  public static Hp48GrobFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 19)
      throw new InvalidDataException("Not a graphics object: too short for a header.");

    if (_IsStringAt(data, 0, "HPHP48-") && data[8] == 30 && data[9] == 43 && (data[10] & 15) == 0)
      return _Binary(data);

    if (_IsStringAt(data, 0, Hp48GrobFile.TextSignature))
      return _Text(data);

    throw new InvalidDataException("Not a graphics object.");
  }

  /// <summary>Reads the binary form, whose fields are nibbles and do not start on byte boundaries.</summary>
  private static Hp48GrobFile _Binary(ReadOnlySpan<byte> data) {
    var nibbles = (data[10] >> 4) | (data[11] << 4) | (data[12] << 12);
    var height = data[13] | (data[14] << 8) | ((data[15] & 15) << 16);
    var width = (data[15] >> 4) | (data[16] << 4) | (data[17] << 12);
    var stride = (width + 7) >> 3;

    // The stored nibble count covers the object's body, which is everything after the first ten
    // and a half bytes; it is the only check the format offers that the header is what it says.
    if (nibbles != data.Length * 2 - 21)
      throw new InvalidDataException($"An object of {nibbles} nibbles is not {data.Length} bytes.");

    if (data.Length != Hp48GrobFile.BinaryBitmapOffset + height * stride)
      throw new InvalidDataException($"An object of {width}x{height} is not {data.Length} bytes.");

    return new() {
      Bitmap = data[Hp48GrobFile.BinaryBitmapOffset..].ToArray(),
      Width = width,
      Height = height,
    };
  }

  /// <summary>Reads the serial-line form, whose bitmap is written as hexadecimal digits.</summary>
  private static Hp48GrobFile _Text(ReadOnlySpan<byte> data) {
    var at = Hp48GrobFile.TextSignature.Length;

    var width = _ParseInt(data, ref at, 10, 65535);
    if (width <= 0 || at >= data.Length || data[at++] != ' ')
      throw new InvalidDataException("An object's width is not followed by a space.");

    var height = _ParseInt(data, ref at, 10, 65535);
    if (height <= 0 || at >= data.Length || data[at++] != '\r')
      throw new InvalidDataException("An object's height is not followed by a carriage return.");

    var stride = (width + 7) >> 3;
    var bitmap = new byte[stride * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < stride; ++x) {
      var high = _Digit(data, ref at);
      var low = _Digit(data, ref at);
      if (high < 0 || low < 0)
        throw new InvalidDataException($"Row {y} ends before the picture does.");

      bitmap[y * stride + x] = (byte)((high << 4) | low);
    }

    return new() { Bitmap = bitmap, Width = width, Height = height, SwappedNibbles = true };
  }

  private static int _ParseInt(ReadOnlySpan<byte> data, ref int at, int radix, int maxValue) {
    var value = _Digit(data, ref at);
    if (value < 0 || value >= radix)
      return -1;

    while (value <= maxValue) {
      var digit = _Digit(data, ref at);
      if (digit < 0)
        return value;

      if (digit >= radix)
        return -1;

      value = value * radix + digit;
    }

    return -1;
  }

  private static int _Digit(ReadOnlySpan<byte> data, ref int at) {
    if (at >= data.Length)
      return -1;

    var c = data[at++];
    if (c is >= (byte)'0' and <= (byte)'9')
      return c - '0';
    if (c is >= (byte)'A' and <= (byte)'F')
      return c - 'A' + 10;
    if (c is >= (byte)'a' and <= (byte)'f')
      return c - 'a' + 10;

    --at;

    return -1;
  }

  private static bool _IsStringAt(ReadOnlySpan<byte> data, int offset, string text) {
    if (offset + text.Length > data.Length)
      return false;

    for (var i = 0; i < text.Length; ++i) {
      if (data[offset + i] != text[i])
        return false;
    }

    return true;
  }

  public static Hp48GrobFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
