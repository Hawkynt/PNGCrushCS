using System;
using System.IO;
using System.Text;

namespace FileFormat.Sixel;

/// <summary>Reads Sixel (DEC terminal graphics) files from bytes, streams, or file paths.</summary>
public static class SixelReader {

  private const byte _ESC = 0x1B;
  private const byte _DCS_8BIT = 0x90;
  private const byte _ST_8BIT = 0x9C;

  public static SixelFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sixel file not found.", file.FullName);

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

  public static SixelFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SixelFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid Sixel stream.");

    var text = Encoding.ASCII.GetString(data);

    var (dcsEnd, aspectRatio, backgroundMode) = _ParseDcs(text);

    var stStart = _FindSt(text, dcsEnd);
    var body = text[dcsEnd..stStart];

    var pixelData = SixelCodec.Decode(body, out var width, out var height, out var palette, out var paletteColorCount);

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

  private static (int BodyStart, int AspectRatio, int BackgroundMode) _ParseDcs(string text) {
    var i = 0;
    if (i < text.Length && text[i] == (char)_DCS_8BIT) {
      ++i;
    } else if (i + 1 < text.Length && text[i] == (char)_ESC && text[i + 1] == 'P') {
      i += 2;
    } else
      throw new InvalidDataException("Invalid DCS introducer.");

    var aspectRatio = 0;
    var backgroundMode = 0;
    var paramIndex = 0;

    while (i < text.Length && text[i] != 'q') {
      if (text[i] >= '0' && text[i] <= '9') {
        var value = 0;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9') {
          value = value * 10 + (text[i] - '0');
          ++i;
        }

        switch (paramIndex) {
          case 0:
            aspectRatio = value;
            break;
          case 1:
            backgroundMode = value;
            break;
        }
      }

      if (i < text.Length && text[i] == ';') {
        ++paramIndex;
        ++i;
      } else if (i < text.Length && text[i] != 'q')
        ++i;
    }

    if (i < text.Length && text[i] == 'q')
      ++i;
    else
      throw new InvalidDataException("Missing 'q' after DCS parameters.");

    return (i, aspectRatio, backgroundMode);
  }

  private static int _FindSt(string text, int start) {
    for (var i = start; i < text.Length; ++i) {
      if (text[i] == (char)_ST_8BIT)
        return i;
      if (i + 1 < text.Length && text[i] == (char)_ESC && text[i + 1] == '\\')
        return i;
    }

    return text.Length;
  }
}
