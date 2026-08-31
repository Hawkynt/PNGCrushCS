using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Tiny;

/// <summary>Reads Tiny Stuff compressed Atari ST pictures from bytes, streams, or file paths.</summary>
public static class TinyReader {

  private const int _PaletteSize = 16 * 2;
  private const int _AnimationOffset = 3;
  private const int _AnimationSize = 4;
  private const int _BaseHeaderSize = 1 + _PaletteSize + 4;

  /// <summary>Reads a Tiny Stuff picture from a file.</summary>
  public static TinyFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Tiny Stuff file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads a Tiny Stuff picture from the current stream position through end-of-stream.</summary>
  public static TinyFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var length = checked((int)(stream.Length - stream.Position));
      var data = new byte[length];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  /// <summary>Parses one complete Tiny Stuff file and rejects truncation or trailing bytes.</summary>
  public static TinyFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _BaseHeaderSize + TinyFile.MinimumControlBytes + 2)
      throw new InvalidDataException("Data is too small for the smallest valid Tiny Stuff picture.");

    var rawResolution = data[0];
    if (rawResolution > 5)
      throw new InvalidDataException($"Invalid Tiny Stuff resolution byte {rawResolution}; expected 0..5.");

    var animated = rawResolution >= _AnimationOffset;
    var resolution = (TinyResolution)(animated ? rawResolution - _AnimationOffset : rawResolution);
    var mode = TinyFile.GetMode(resolution);
    var at = 1;

    byte animationLimits = 0;
    sbyte animationSpeedDirection = 0;
    ushort animationDuration = 0;
    if (animated) {
      if (data.Length < at + _AnimationSize)
        throw new InvalidDataException("Tiny Stuff colour-rotation extension is truncated.");

      animationLimits = data[at++];
      animationSpeedDirection = unchecked((sbyte)data[at++]);
      animationDuration = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
      at += 2;
    }

    if (data.Length < at + _PaletteSize + 4)
      throw new InvalidDataException("Tiny Stuff header is truncated before its palette or stream lengths.");

    var palette = new short[16];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(data[(at + i * 2)..]);
    at += _PaletteSize;

    var controlCount = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
    var dataWords = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
    at += 4;

    if (controlCount is < TinyFile.MinimumControlBytes or > TinyFile.MaximumControlBytes)
      throw new InvalidDataException($"Tiny Stuff control block length {controlCount} is outside {TinyFile.MinimumControlBytes}..{TinyFile.MaximumControlBytes}.");
    if (dataWords is < 1 or > TinyFile.ScreenWordCount)
      throw new InvalidDataException($"Tiny Stuff data-word count {dataWords} is outside 1..{TinyFile.ScreenWordCount}.");

    var expectedLength = checked(at + controlCount + dataWords * 2);
    if (data.Length != expectedLength)
      throw new InvalidDataException($"Tiny Stuff file length is {data.Length} bytes but its header requires exactly {expectedLength}.");

    var control = data.Slice(at, controlCount);
    var words = data.Slice(at + controlCount, dataWords * 2);
    var file = new TinyFile {
      Width = mode.Width,
      Height = mode.Height,
      Resolution = resolution,
      HasColorAnimation = animated,
      AnimationLimits = animationLimits,
      AnimationSpeedDirection = animationSpeedDirection,
      AnimationDuration = animationDuration,
      Palette = palette,
      PixelData = TinyCompressor.Decompress(control, words),
    };

    try {
      TinyFile.Validate(file, nameof(data));
    } catch (ArgumentException exception) {
      throw new InvalidDataException(exception.Message, exception);
    }

    return file;
  }

  /// <summary>Reads a Tiny Stuff picture from a byte array.</summary>
  public static TinyFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
