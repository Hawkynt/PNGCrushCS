using System;
using System.IO;

namespace FileFormat.Mapletown;

/// <summary>The bit stream a Mapletown picture is written in.</summary>
/// <remarks>
/// Two forms exist. ML1 is binary and takes eight bits from every byte; MX1 was made to survive a
/// bulletin board and takes six from every printable character, skipping the punctuation a board
/// might have eaten. Everything above the bit level is the same in both.
/// </remarks>
public ref struct MapletownStream {

  private readonly ReadOnlySpan<byte> _data;
  private readonly byte[]? _decode;
  private int _bits;

  /// <summary>Creates a stream over binary bytes.</summary>
  public MapletownStream(ReadOnlySpan<byte> data, int at) {
    this._data = data;
    this.At = at;
  }

  /// <summary>Creates a stream over printable characters, six bits to each.</summary>
  public MapletownStream(ReadOnlySpan<byte> data, int at, byte[] decode) {
    this._data = data;
    this.At = at;
    this._decode = decode;
  }

  public int At;

  /// <summary>Restarts the bit accumulator, which a new image in a text file needs.</summary>
  public void Realign() => this._bits = 0;

  /// <summary>The table mapping a character to the six bits it carries.</summary>
  /// <remarks>
  /// The alphabet is what a Japanese bulletin board would pass through unaltered: printable ASCII
  /// less the six characters a quoting or escaping layer might touch, then part of the half-width
  /// Japanese range to make the count up.
  /// </remarks>
  public static byte[] CreateDecodeTable() {
    var table = new byte[256];
    var next = 0;

    for (var c = 0; c < 256; ++c)
      table[c] = c is (>= '!' and <= '~') and not ('"' or '\'' or ',' or '@' or '\\' or '`')
                 || c is >= 161 and <= 200
        ? (byte)next++
        : (byte)128;

    return table;
  }

  public int Bit() {
    if (this._decode == null) {
      if ((this._bits & 127) == 0) {
        if (this.At >= this._data.Length)
          return -1;

        this._bits = (this._data[this.At++] << 1) | 1;
      } else
        this._bits <<= 1;

      return (this._bits >> 8) & 1;
    }

    if ((this._bits & 63) == 0) {
      var c = this._Character();
      if (c < 0)
        return -1;

      var value = this._decode[c];
      if (value >= 128)
        return -1;

      this._bits = (value << 1) | 1;
    } else
      this._bits <<= 1;

    return (this._bits >> 7) & 1;
  }

  /// <summary>
  /// Reads one character of the text form, skipping the spacing a board may have introduced and
  /// decoding the three-byte sequences that stand for characters a plain byte could not carry.
  /// </summary>
  private int _Character() {
    int c;
    do {
      if (this.At >= this._data.Length)
        return -1;

      c = this._data[this.At++];
    } while (c is '\r' or '\n' or ' ');

    if (c != 0xEF)
      return c;

    if (this.At + 1 >= this._data.Length)
      return -1;

    switch (this._data[this.At++]) {
      case 0xBD: {
        var next = this._data[this.At++];
        return next is >= 160 and <= 191 ? next : -1;
      }

      case 0xBE: {
        var next = this._data[this.At++];
        return next is >= 128 and <= 159 ? next + 64 : -1;
      }

      default:
        return -1;
    }
  }

  public int Bits(int count) {
    var result = 0;
    while (--count >= 0) {
      var bit = this.Bit();
      if (bit < 0)
        return -1;

      result = (result << 1) | bit;
    }

    return result;
  }

  /// <summary>
  /// Reads a length, whose width is written first as that many ones before a zero — so a run of
  /// one costs two bits and a run of a thousand costs twenty-one.
  /// </summary>
  public int Length() {
    for (var bits = 1; bits < 21; ++bits) {
      var bit = this.Bit();
      if (bit < 0)
        return -1;

      if (bit != 0)
        continue;

      var value = this.Bits(bits);

      return value < 0 ? -1 : value + (1 << bits) - 1;
    }

    return -1;
  }
}

/// <summary>Decodes one Mapletown image out of a stream.</summary>
public static class MapletownDecoder {

  /// <summary>What every image begins with.</summary>
  public const int Signature = 825241626;

  /// <summary>Colours a palette entry can name: nine levels in each of three channels.</summary>
  private const int _COLOR_SPACE = 729;

  /// <summary>
  /// What an untouched pixel holds. No colour can produce it, every channel being a multiple of
  /// nine parts of 255, so it can stand for "nothing has been drawn here yet".
  /// </summary>
  private const int _BLANK = 1;

  /// <summary>Reads one image into the picture, and returns how tall it was.</summary>
  /// <param name="pixels">
  /// The picture so far, or null to have one made the size of this image.
  /// </param>
  public static int Decode(
    ref MapletownStream stream, ref int[]? pixels, ref int width, ref int height, int imageOffset) {
    if (stream.Bits(32) != Signature || stream.Bits(32) < 0 || stream.Bits(16) < 0)
      return -1;

    var left = stream.Bits(16);
    var top = stream.Bits(16);
    var imageWidth = stream.Bits(16) - left + 1;
    var imageHeight = stream.Bits(16) - top + 1;
    if (imageWidth <= 0 || imageHeight <= 0)
      return -1;

    // A block of the header this decoder has no use for; it describes the drawing program's state.
    for (var i = 0; i < 624; ++i) {
      if (stream.Bit() < 0)
        return -1;
    }

    var mode = stream.Bits(2);
    int lastColor;
    switch (mode) {
      case 0: lastColor = 127; break;
      case 1 or 2: lastColor = stream.Bits(7); break;
      default: return -1;
    }

    if (imageOffset < 0) {
      width = imageWidth;
      height = imageHeight;
      pixels = new int[width * height];
      imageOffset = 0;
    }

    if (pixels == null)
      return -1;

    // Every image brings its own palette, so the last one's colours do not carry over.
    var palette = new int[128];
    for (var i = 0; i <= lastColor; ++i) {
      // Two of the three modes name which entry they are filling; the third fills them in order.
      var entry = mode > 0 ? stream.Bits(7) : 0;
      var color = stream.Bits(10);
      if (color is < 0 or >= _COLOR_SPACE)
        return -1;

      palette[mode == 1 ? entry : i] = _Color(color);
    }

    for (var y = 0; y < imageHeight; ++y)
    for (var x = 0; x < imageWidth; ++x)
      pixels[imageOffset + y * width + x] = _BLANK;

    var distance = 1;
    var rgb = 0;

    for (var y = 0; y < imageHeight; ++y)
    for (var x = 0; x < imageWidth; ++x) {
      var offset = imageOffset + y * width + x;

      // A run in progress paints forward — except where a chain has already been here, in which
      // case the run takes that colour instead of overwriting it. That is how the format draws a
      // shape's outline before its fill and lets the fill stop at it.
      if (--distance > 0) {
        var existing = pixels[offset];
        if (existing == _BLANK)
          pixels[offset] = rgb;
        else
          rgb = existing;

        continue;
      }

      distance = stream.Length();
      if (distance < 0)
        return -1;

      var index = mode == 2 ? stream.Length() - 1 : stream.Bits(7);
      if (index is < 0 or >= 128)
        return -1;

      rgb = palette[index];

      switch (stream.Bit()) {
        case 0: break;
        case 1:
          if (!_Chain(ref stream, pixels, offset, rgb, width, height))
            return -1;

          break;

        default: return -1;
      }

      pixels[offset] = rgb;
    }

    return distance == 1 && stream.Length() == imageWidth * imageHeight + 1 ? imageHeight : -1;
  }

  /// <summary>
  /// Draws a chain: a stroke that walks down the picture a row at a time, stepping sideways by up
  /// to two as it goes, until it says it has finished.
  /// </summary>
  private static bool _Chain(
    ref MapletownStream stream, int[] pixels, int offset, int rgb, int width, int height) {
    for (;;) {
      switch (stream.Bit()) {
        case 0: break;

        case 1:
          switch (stream.Bits(2)) {
            case 0: ++offset; break;
            case 1: --offset; break;
            case 2: return true;

            case 3:
              switch (stream.Bit()) {
                case 0: offset += 2; break;
                case 1: offset -= 2; break;
                default: return false;
              }

              break;

            default: return false;
          }

          break;

        default: return false;
      }

      offset += width;
      if (offset < 0 || offset >= width * height)
        return false;

      pixels[offset] = rgb;
    }
  }

  /// <summary>Expands a colour, which is a number in base nine with one digit a channel.</summary>
  private static int _Color(int value) {
    var red = value / 81 * 255 >> 3;
    var green = value / 9 % 9 * 255 >> 3;
    var blue = value % 9 * 255 >> 3;

    return (red << 16) | (green << 8) | blue;
  }

  /// <summary>Turns the decoded picture into RGB triplets.</summary>
  public static byte[] ToRgb(int[] pixels) {
    var rgb = new byte[pixels.Length * 3];
    for (var i = 0; i < pixels.Length; ++i) {
      rgb[i * 3] = (byte)(pixels[i] >> 16);
      rgb[i * 3 + 1] = (byte)(pixels[i] >> 8);
      rgb[i * 3 + 2] = (byte)pixels[i];
    }

    return rgb;
  }

  /// <summary>Finds the next image in a text file, which a marked line announces.</summary>
  public static bool FindImage(ref MapletownStream stream, ReadOnlySpan<byte> data) {
    for (;;) {
      var start = stream.At;
      for (;;) {
        if (stream.At >= data.Length)
          return false;

        var c = data[stream.At++];
        if (c is (byte)'\r' or (byte)'\n')
          break;
      }

      if (stream.At - start < 17 || !_IsStringAt(data, start, "@@@ ")
          || !_IsStringAt(data, stream.At - 11, "lines) @@@"))
        continue;

      stream.Realign();

      return true;
    }
  }

  private static bool _IsStringAt(ReadOnlySpan<byte> data, int offset, string text) {
    if (offset < 0 || offset + text.Length > data.Length)
      return false;

    for (var i = 0; i < text.Length; ++i) {
      if (data[offset + i] != text[i])
        return false;
    }

    return true;
  }

  /// <summary>Throws with the given reason, so callers can be expressions.</summary>
  public static void Fail(string reason) => throw new InvalidDataException(reason);
}
