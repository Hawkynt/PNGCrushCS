using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Sixel;

/// <summary>Reads SIXEL (DEC terminal graphics) files from bytes, streams, or file paths.</summary>
public static class SixelReader {

  private const byte _ESC = 0x1B;
  private const byte _DCS_8BIT = 0x90;
  private const byte _ST_8BIT = 0x9C;

  public static SixelFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SIXEL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SixelFile FromStream(Stream stream) {
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

  public static SixelFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SixelFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid SIXEL stream.");

    var (bodyStart, aspectRatio, backgroundMode) = _ParseDcs(data);
    var bodyEnd = _FindSt(data, bodyStart);
    var body = Encoding.ASCII.GetString(data[bodyStart..bodyEnd]);

    var pixelData = SixelCodec.Decode(
      body,
      out var width,
      out var height,
      out var palette,
      out var paletteColorCount,
      backgroundMode
    );

    return new SixelFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = paletteColorCount,
      AspectRatio = aspectRatio,
      BackgroundMode = backgroundMode
    };
  }

  private static (int BodyStart, int AspectRatio, int BackgroundMode) _ParseDcs(ReadOnlySpan<byte> data) {
    var i = 0;
    if (data[i] == _DCS_8BIT)
      ++i;
    else if (i + 1 < data.Length && data[i] == _ESC && data[i + 1] == (byte)'P')
      i += 2;
    else
      throw new InvalidDataException("Invalid DCS introducer.");

    var parameters = new List<int>(3);
    var value = 0;
    var hasDigits = false;

    while (i < data.Length) {
      var b = data[i++];
      if (b is >= (byte)'0' and <= (byte)'9') {
        hasDigits = true;
        try {
          value = checked(value * 10 + b - (byte)'0');
        } catch (OverflowException exception) {
          throw new InvalidDataException("SIXEL DCS parameter is too large.", exception);
        }
        continue;
      }

      if (b == (byte)';') {
        parameters.Add(hasDigits ? value : 0);
        value = 0;
        hasDigits = false;
        continue;
      }

      if (b != (byte)'q')
        throw new InvalidDataException($"Invalid byte 0x{b:X2} in SIXEL DCS parameters.");

      parameters.Add(hasDigits ? value : 0);
      var aspectRatio = parameters.Count > 0 ? parameters[0] : 0;
      var backgroundMode = parameters.Count > 1 ? parameters[1] : 0;
      if (backgroundMode is not (0 or 1 or 2))
        throw new InvalidDataException($"Invalid SIXEL background mode P2={backgroundMode}.");
      return (i, aspectRatio, backgroundMode);
    }

    throw new InvalidDataException("Missing 'q' after SIXEL DCS parameters.");
  }

  private static int _FindSt(ReadOnlySpan<byte> data, int start) {
    for (var i = start; i < data.Length; ++i) {
      if (data[i] == _ST_8BIT)
        return i;
      if (i + 1 < data.Length && data[i] == _ESC && data[i + 1] == (byte)'\\')
        return i;
    }

    throw new InvalidDataException("Missing SIXEL string terminator.");
  }
}
