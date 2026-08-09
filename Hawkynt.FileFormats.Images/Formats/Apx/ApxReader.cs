using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Apx;

/// <summary>Reads Ability Photopaint images (.apx) from bytes, streams, or file paths.</summary>
public static class ApxReader {

  /// <summary>Three words behind the signature that the format's own reader does not read.</summary>
  private const int _UNREAD_WORDS_BEFORE_STEP = 3;

  /// <summary>The constant part of the step the two words after those describe.</summary>
  private const int _STEP_CONSTANT = 0x28;

  /// <summary>Two words behind the layer count that the format's own reader does not read.</summary>
  private const int _UNREAD_WORDS_AFTER_COUNT = 2;

  /// <summary>A fixed run stepped over between the header and the first layer record.</summary>
  private const int _LAYER_TABLE_GAP = 0x10;

  /// <summary>Four words at the front of a layer record that the format's own reader does not read.</summary>
  private const int _LAYER_WORDS_BEFORE_NAME = 4;

  /// <summary>Three words behind a layer's name that the format's own reader does not read.</summary>
  private const int _LAYER_WORDS_AFTER_NAME = 3;

  /// <summary>More layers than a paint document would ever hold; a guard against a wild count.</summary>
  private const int _MAXIMUM_LAYER_COUNT = 4096;

  public static ApxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ability Photopaint image not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ApxFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static ApxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ApxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ApxFile.SignatureSize)
      throw new InvalidDataException($"Data too small for an Ability Photopaint image (need at least {ApxFile.SignatureSize} bytes, got {data.Length}).");

    var signature = data[..ApxFile.SignatureSize];
    var isPro = signature.SequenceEqual(ApxFile.MagicPaintPro);
    if (!isPro && !signature.SequenceEqual(ApxFile.MagicPaint))
      throw new InvalidDataException("Not an Ability Photopaint image: the 21 bytes it opens with are neither of the two signatures this format uses.");

    var at = (long)ApxFile.SignatureSize + _UNREAD_WORDS_BEFORE_STEP * 4L;
    var a = _Word(data, ref at);
    var b = _Word(data, ref at);
    var stepped = (ulong)a * b;
    if (stepped > (ulong)data.Length)
      throw new InvalidDataException($"An Ability Photopaint header steps over {stepped} entries and the file has {data.Length} bytes.");

    _Step(data, ref at, (long)stepped * 4 + _STEP_CONSTANT);

    var resolution = _Word(data, ref at);
    var width = _Word(data, ref at);
    var height = _Word(data, ref at);
    var layers = _Word(data, ref at);

    if (layers == 0)
      throw new InvalidDataException("An Ability Photopaint image holding no layer holds no picture either.");

    if (layers > _MAXIMUM_LAYER_COUNT)
      throw new InvalidDataException($"An Ability Photopaint image states {layers} layers, which is not a count this reads.");

    if (width is 0 or > ApxFile.MaximumSide || height is 0 or > ApxFile.MaximumSide)
      throw new InvalidDataException($"Invalid Ability Photopaint dimensions: {width}x{height}.");

    _Step(data, ref at, _UNREAD_WORDS_AFTER_COUNT * 4L + _LAYER_TABLE_GAP);

    for (var i = 0; i < layers; ++i) {
      _Step(data, ref at, _LAYER_WORDS_BEFORE_NAME * 4L);
      var name = _Word(data, ref at);
      _Step(data, ref at, name);
      _Step(data, ref at, _LAYER_WORDS_AFTER_NAME * 4L);
    }

    var stride = (long)width * ApxFile.BytesPerPixel;
    var needed = stride * height;
    if (data.Length - at < needed)
      throw new InvalidDataException($"A {width}x{height} Ability Photopaint picture needs {needed} bytes and the file has {data.Length - at} behind its header.");

    // The file stores its rows bottom to top and its bytes alpha, blue, green, red; the library wants
    // them top to bottom and red, green, blue, alpha, so both orders are turned around here.
    var pixels = new byte[needed];
    for (var y = 0; y < height; ++y) {
      var source = (int)(at + (height - 1 - y) * stride);
      var target = (int)(y * stride);
      for (var x = 0; x < width; ++x) {
        var from = source + x * ApxFile.BytesPerPixel;
        var to = target + x * ApxFile.BytesPerPixel;
        pixels[to] = data[from + 3];
        pixels[to + 1] = data[from + 2];
        pixels[to + 2] = data[from + 1];
        pixels[to + 3] = data[from];
      }
    }

    return new() {
      Width = (int)width,
      Height = (int)height,
      Resolution = (int)resolution,
      LayerCount = (int)layers,
      IsPro = isPro,
      PixelData = pixels,
    };
  }

  private static uint _Word(ReadOnlySpan<byte> data, ref long at) {
    if (at + 4 > data.Length)
      throw new InvalidDataException($"An Ability Photopaint header wants a word at byte {at} and the file has {data.Length}.");

    var value = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)at..]);
    at += 4;
    return value;
  }

  private static void _Step(ReadOnlySpan<byte> data, ref long at, long count) {
    at += count;
    if (at < 0 || at > data.Length)
      throw new InvalidDataException($"An Ability Photopaint header steps to byte {at} and the file has {data.Length}.");
  }
}
