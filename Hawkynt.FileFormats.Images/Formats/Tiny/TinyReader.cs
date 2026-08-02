using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>Reads Tiny (compressed DEGAS) files from bytes, streams, or file paths.</summary>
public static class TinyReader {

  /// <summary>Bytes the palette takes.</summary>
  private const int _PALETTE_SIZE = 16 * 2;

  /// <summary>What is added to the resolution byte to say the picture cycles its colours.</summary>
  private const int _ANIMATION_OFFSET = 3;

  /// <summary>Bytes of animation settings that then sit before the palette.</summary>
  private const int _ANIMATION_SIZE = 4;

  /// <summary>Resolution byte, palette, and the two block lengths.</summary>
  private const int _HEADER_SIZE = 1 + _PALETTE_SIZE + 4;

  public static TinyFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Tiny file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TinyFile FromStream(Stream stream) {
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

  public static TinyFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid Tiny file.");

    // Three of the six resolution bytes say the picture cycles its colours, and put four bytes of
    // animation settings between the byte and the palette. The picture itself is the same either way.
    var resolutionByte = data[0];
    var animated = resolutionByte >= _ANIMATION_OFFSET;
    if (animated)
      resolutionByte -= _ANIMATION_OFFSET;

    if (resolutionByte > 2)
      throw new InvalidDataException($"Invalid Tiny resolution value: {data[0]}.");

    var resolution = (TinyResolution)resolutionByte;
    var (width, height) = _GetFormatInfo(resolution);

    var at = 1 + (animated ? _ANIMATION_SIZE : 0);
    if (data.Length < at + _PALETTE_SIZE + 4)
      throw new InvalidDataException("A Tiny file states a palette and two block lengths it does not carry.");

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(data[(at + i * 2)..]);

    at += _PALETTE_SIZE;

    // The header states the two block lengths: control bytes first, then how many words of data.
    var controlCount = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
    var dataWords = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
    at += 4;

    if (controlCount == 0 || data.Length < at + controlCount)
      throw new InvalidDataException($"A Tiny file states {controlCount} control bytes; the file holds {data.Length - at}.");

    var control = data.Slice(at, controlCount);
    var available = Math.Min(dataWords * 2, data.Length - at - controlCount);
    if (available <= 0)
      throw new InvalidDataException("A Tiny file states no data words.");

    var words = data.Slice(at + controlCount, available);
    var pixelData = TinyCompressor.Decompress(control, words);

    return new TinyFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }

  public static TinyFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static (int Width, int Height) _GetFormatInfo(TinyResolution resolution) => resolution switch {
    TinyResolution.Low => (320, 200),
    TinyResolution.Medium => (640, 200),
    TinyResolution.High => (640, 400),
    _ => throw new InvalidDataException($"Unknown Tiny resolution: {resolution}.")
  };
}
