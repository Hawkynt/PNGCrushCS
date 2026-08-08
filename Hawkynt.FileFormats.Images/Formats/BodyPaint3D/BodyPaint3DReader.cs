using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.BodyPaint3D;

/// <summary>Walks a BodyPaint 3D texture's tag stream and decompresses the bitmap it carries.</summary>
public static class BodyPaint3DReader {

  public static BodyPaint3DFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BodyPaint 3D texture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BodyPaint3DFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static BodyPaint3DFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static BodyPaint3DFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BodyPaint3DFile.Magic.Length)
      throw new InvalidDataException("Data too small to be a BodyPaint 3D texture.");
    if (!data[..BodyPaint3DFile.Magic.Length].SequenceEqual(BodyPaint3DFile.Magic))
      throw new InvalidDataException("Not a BodyPaint 3D texture: the file does not open with AC4DBody.");

    var at = BodyPaint3DFile.Magic.Length;
    int width = 0, height = 0;

    while (at < data.Length) {
      var tag = data[at];

      // Only two of the record classes matter. Everything else — the document root, the layer
      // properties, the two numbered records a layered file carries — is walked past by the same
      // tag reader, which is what proves the layout: an unknown tag would strand the walk rather
      // than let it reach the end of the file.
      if (tag == BodyPaint3DFile.TagBegin) {
        var (klass, _) = _ReadRecordHeader(data, ref at);

        if (klass == BodyPaint3DFile.ClassTexture) {
          (width, height) = _ReadTextureHeader(data, ref at);
          continue;
        }

        if (klass != BodyPaint3DFile.ClassBitmap)
          continue;

        var bitmap = _ReadBitmap(data, ref at, width, height);
        if (bitmap != null)
          return bitmap.Value;

        continue;
      }

      _SkipValue(data, ref at);
    }

    throw new InvalidDataException("A BodyPaint 3D texture carries no bitmap the texture header accounts for.");
  }

  /// <summary>Reads a record's class and subtype. The subtype varies without the layout doing so.</summary>
  private static (uint Class, uint Subtype) _ReadRecordHeader(ReadOnlySpan<byte> data, ref int at) {
    _Need(data, at, 9, "a record header");
    var klass = _ReadUInt32(data, at + 1);
    var subtype = _ReadUInt32(data, at + 5);
    at += 9;
    return (klass, subtype);
  }

  /// <summary>Reads the three integers <c>BdTx</c> states: width, height and a colour mode.</summary>
  /// <remarks>
  /// The colour mode is read past rather than acted on. It is 2 wherever the bitmap has one channel
  /// and 4 wherever it has three, which matches Cinema 4D's own enumeration, but those numbers are
  /// not published, so the channel count is taken from the bitmap record that states it outright.
  /// </remarks>
  private static (int Width, int Height) _ReadTextureHeader(ReadOnlySpan<byte> data, ref int at) {
    var width = _ReadInt32Value(data, ref at);
    var height = _ReadInt32Value(data, ref at);
    _ReadInt32Value(data, ref at);

    if (width <= 0 || width > BodyPaint3DFile.MaxDimension)
      throw new InvalidDataException($"A BodyPaint 3D texture states a width of {width}.");
    if (height <= 0 || height > BodyPaint3DFile.MaxDimension)
      throw new InvalidDataException($"A BodyPaint 3D texture states a height of {height}.");

    return (width, height);
  }

  /// <summary>
  /// Reads one <c>BdVx</c>: its rectangle, its channel count, and then a scanline record per row
  /// per channel until the record's close. Answers null for a bitmap that is not the document's —
  /// one with no scanlines, or one whose rectangle is not the size the texture header states.
  /// </summary>
  private static BodyPaint3DFile? _ReadBitmap(ReadOnlySpan<byte> data, ref int at, int width, int height) {
    var x0 = _ReadInt32Value(data, ref at);
    var y0 = _ReadInt32Value(data, ref at);
    var x1 = _ReadInt32Value(data, ref at);
    var y1 = _ReadInt32Value(data, ref at);
    var planes = _ReadInt32Value(data, ref at);

    var rows = new List<byte[]>();
    while (at < data.Length && data[at] == BodyPaint3DFile.TagScanline)
      rows.Add(_ReadScanline(data, ref at));

    if (rows.Count == 0)
      return null;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("A BodyPaint 3D bitmap arrives before the texture header states a size.");

    // The rectangle is an origin and a corner. Every sample states the whole texture; a layer that
    // covered part of one would need placement rules nothing here has ever seen, so it is refused
    // rather than positioned by guesswork.
    if (x0 != 0 || y0 != 0 || x1 - x0 != width || y1 - y0 != height)
      return null;

    if (planes != BodyPaint3DFile.GrayPlanes && planes != BodyPaint3DFile.RgbPlanes)
      throw new InvalidDataException($"A BodyPaint 3D bitmap states {planes} channels; only 1 and 3 are known.");

    var expected = height * planes;
    if (rows.Count != expected)
      throw new InvalidDataException($"A {width}x{height} bitmap in {planes} channels wants {expected} scanlines; the record holds {rows.Count}.");

    var pixels = new byte[width * height * planes];
    for (var row = 0; row < rows.Count; ++row) {
      var line = rows[row];
      if (line.Length != width)
        throw new InvalidDataException($"Scanline {row} decompresses to {line.Length} bytes where the texture is {width} wide.");

      // Row k of the stream is channel k mod planes of picture row k div planes.
      var y = row / planes;
      var channel = row % planes;
      var target = (y * width) * planes + channel;
      for (var x = 0; x < width; ++x, target += planes)
        pixels[target] = line[x];
    }

    return new() { Width = width, Height = height, Planes = planes, PixelData = pixels };
  }

  /// <summary>Reads one compressed scanline: a method byte and then the PackBits stream.</summary>
  private static byte[] _ReadScanline(ReadOnlySpan<byte> data, ref int at) {
    _Need(data, at, 2, "a scanline");
    var method = data[at + 1];
    if (method != BodyPaint3DFile.MethodPackBits)
      throw new InvalidDataException($"A BodyPaint 3D scanline states compression {method}; only {BodyPaint3DFile.MethodPackBits} is known.");

    at += 2;
    if (at >= data.Length || data[at] != BodyPaint3DFile.TagByteArray)
      throw new InvalidDataException("A BodyPaint 3D scanline is not followed by the byte array holding it.");

    var packed = _ReadByteArray(data, ref at);
    return _UnpackBits(packed);
  }

  /// <summary>PackBits as TIFF defines it, over one row.</summary>
  private static byte[] _UnpackBits(ReadOnlySpan<byte> packed) {
    var output = new List<byte>(packed.Length * 2);

    var at = 0;
    while (at < packed.Length) {
      int control = packed[at++];

      if (control == 0x80)
        continue;

      if (control < 0x80) {
        var count = control + 1;
        if (at + count > packed.Length)
          throw new InvalidDataException("A PackBits literal run reaches past the end of its scanline.");

        for (var i = 0; i < count; ++i)
          output.Add(packed[at + i]);

        at += count;
        continue;
      }

      if (at >= packed.Length)
        throw new InvalidDataException("A PackBits repeat run states no byte to repeat.");

      var repeat = 257 - control;
      var value = packed[at++];
      for (var i = 0; i < repeat; ++i)
        output.Add(value);
    }

    return [.. output];
  }

  /// <summary>Steps over one value of whatever tag stands at the position.</summary>
  private static void _SkipValue(ReadOnlySpan<byte> data, ref int at) {
    switch (data[at]) {
      case BodyPaint3DFile.TagEnd:
        ++at;
        return;
      case BodyPaint3DFile.TagByte:
        at += 2;
        return;
      case BodyPaint3DFile.TagInt32:
      case BodyPaint3DFile.TagFloat32:
        at += 5;
        return;
      case BodyPaint3DFile.TagByteArray:
      case BodyPaint3DFile.TagWideString:
        _ReadByteArray(data, ref at);
        return;
      case BodyPaint3DFile.TagScanline:
        _ReadScanline(data, ref at);
        return;
      default:
        throw new InvalidDataException($"Unknown tag 0x{data[at]:X2} at {at} in a BodyPaint 3D texture.");
    }
  }

  private static int _ReadInt32Value(ReadOnlySpan<byte> data, ref int at) {
    _Need(data, at, 5, "an integer");
    if (data[at] != BodyPaint3DFile.TagInt32)
      throw new InvalidDataException($"Expected an integer at {at} in a BodyPaint 3D texture, found tag 0x{data[at]:X2}.");

    var value = (int)_ReadUInt32(data, at + 1);
    at += 5;
    return value;
  }

  private static byte[] _ReadByteArray(ReadOnlySpan<byte> data, ref int at) {
    _Need(data, at, 5, "a byte array");
    var length = _ReadUInt32(data, at + 1);
    at += 5;

    if (length > (uint)(data.Length - at))
      throw new InvalidDataException($"A byte array of {length} bytes reaches past the end of a BodyPaint 3D texture.");

    var result = data.Slice(at, (int)length).ToArray();
    at += (int)length;
    return result;
  }

  private static uint _ReadUInt32(ReadOnlySpan<byte> data, int at)
    => ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];

  private static void _Need(ReadOnlySpan<byte> data, int at, int count, string what) {
    if (at + count > data.Length)
      throw new InvalidDataException($"A BodyPaint 3D texture ends in the middle of {what}.");
  }
}
