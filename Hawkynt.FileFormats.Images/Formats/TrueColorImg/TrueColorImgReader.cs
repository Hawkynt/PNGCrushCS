using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.TrueColorImg;

/// <summary>Reads true-colour GEM bit images from bytes, streams, or file paths.</summary>
public static class TrueColorImgReader {

  public static TrueColorImgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TrueColorImgFile FromStream(Stream stream) {
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

  public static TrueColorImgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 17 || data[0] != 0 || data[1] > 3 || data[4] != 0)
      throw new InvalidDataException("Not a GEM bit image.");

    var headerLength = ((data[2] << 8) | data[3]) << 1;
    if (headerLength < 16 || headerLength >= data.Length)
      throw new InvalidDataException("A GEM bit image's header is not where it says.");

    int bitplanes = data[5];
    var width = (data[12] << 8) | data[13];
    var height = (data[14] << 8) | data[15];
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A picture of {width}x{height} is empty.");

    // The one variant that stores whole pixels rather than bitplanes.
    if (headerLength == 18 && data[16] == 0 && data[17] == 3)
      return _Chunky(data, width, height);

    if (headerLength != 28 || data[16] != 'T' || data[17] != 'I' || data[18] != 'M' || data[19] != 'G'
        || data[20] != 0 || data[21] != 3 || data[22] != 0 || data[24] != 0 || data[26] != 0)
      throw new InvalidDataException("Not a true-colour GEM bit image.");

    // The three channel widths, which have to agree with the plane count. Sixteen planes is
    // five-six-five and not five-five-five: the spare bit goes to green, the eye having most of
    // its resolution there.
    var shape = (bitplanes << 24) | (data[23] << 16) | (data[25] << 8) | data[27];
    if (shape is not (0x0F050505 or 0x10050605 or 0x18080808))
      throw new InvalidDataException($"{bitplanes} planes of {data[23]}, {data[25]}, {data[27]} bits is not a colour.");

    return _Bitplanes(data, headerLength, bitplanes, width, height);
  }

  /// <summary>
  /// The chunky variant: a marker, a count, and that many pixels of three bytes each, blue first.
  /// </summary>
  private static TrueColorImgFile _Chunky(ReadOnlySpan<byte> data, int width, int height) {
    var rgb = new byte[width * height * 3];
    var at = 18;
    var left = 0;

    for (var i = 0; i < width * height; ++i) {
      if (left == 0) {
        if (at + 1 >= data.Length || data[at++] != 128)
          throw new InvalidDataException("A chunky picture's runs are not where they should be.");

        left = data[at++];
        if (left == 0)
          throw new InvalidDataException("A chunky run covers no pixels.");
      }

      if (at + 2 >= data.Length)
        throw new InvalidDataException("A chunky picture ends before its pixels do.");

      rgb[i * 3] = data[at + 2];
      rgb[i * 3 + 1] = data[at + 1];
      rgb[i * 3 + 2] = data[at];
      at += 3;
      --left;
    }

    return new() { Width = width, Height = height, Pixels = rgb };
  }

  /// <summary>The bitplane variants, packed by GEM's own line coder.</summary>
  private static TrueColorImgFile _Bitplanes(
    ReadOnlySpan<byte> data, int headerLength, int bitplanes, int width, int height) {
    var bytesPerPlane = (width + 7) >> 3;
    var stride = bitplanes * bytesPerPlane;
    var line = new byte[stride];
    var rgb = new byte[width * height * 3];

    var stream = new _Stream(data, headerLength, (data[6] << 8) | data[7]);

    for (var y = 0; y < height;) {
      // A line may say it stands for several, which is the only compression across rows.
      var repeat = Math.Min(stream.LineRepeatCount(), height - y);
      stream.UnpackLine(line, y == 0);

      for (var x = 0; x < width; ++x) {
        var value = 0;
        for (var plane = bitplanes; --plane >= 0;)
          value = (value << 1) | ((line[plane * bytesPerPlane + (x >> 3)] >> (~x & 7)) & 1);

        var color = bitplanes switch {
          15 => _FiveBitChannels(value, 19, 6, 7),
          16 => _SixBitGreen(value),
          _ => ((value & 255) << 16) | (value & 0xFF00) | (value >> 16),
        };

        for (var i = 0; i < repeat; ++i) {
          var target = ((y + i) * width + x) * 3;
          rgb[target] = (byte)(color >> 16);
          rgb[target + 1] = (byte)(color >> 8);
          rgb[target + 2] = (byte)color;
        }
      }

      y += repeat;
    }

    return new() { Width = width, Height = height, Pixels = rgb };
  }

  /// <summary>Expands a fifteen-bit colour, whose channels are stored blue first.</summary>
  private static int _FiveBitChannels(int value, int blueShift, int greenShift, int redShift) {
    var color = ((value & 31) << blueShift) | ((value & 992) << greenShift) | ((value >> redShift) & 248);

    return color | ((color >> 5) & 0x070707);
  }

  /// <summary>
  /// Expands a sixteen-bit colour, whose green has the extra bit — and whose lowest green bit is
  /// filled from a different place than the other two channels' are, there being one more of it.
  /// </summary>
  private static int _SixBitGreen(int value) {
    var color = ((value & 31) << 19) | ((value & 2016) << 5) | ((value >> 8) & 248);

    return color | ((color >> 5) & 0x070007) | ((color >> 6) & 0x000300);
  }

  /// <summary>GEM's line coder: solid runs, literal runs, repeated patterns, and repeated lines.</summary>
  private ref struct _Stream {

    private readonly ReadOnlySpan<byte> _data;
    private readonly int _patternLength;
    private int _at;
    private int _count;
    private int _value;
    private int _patternRepeats;

    public _Stream(ReadOnlySpan<byte> data, int at, int patternLength) {
      this._data = data;
      this._at = at;
      this._patternLength = patternLength;
      this._value = -1;
    }

    private int _Byte() => this._at < this._data.Length ? this._data[this._at++] : -1;

    /// <summary>How many rows the line about to be read stands for.</summary>
    public int LineRepeatCount() {
      if (this._count != 0 || this._at >= this._data.Length - 4
          || this._data[this._at] != 0 || this._data[this._at + 1] != 0 || this._data[this._at + 2] != 255)
        return 1;

      this._at += 4;

      return this._data[this._at - 1];
    }

    /// <summary>
    /// Reads one line. A byte the coder marks as unchanged keeps what the previous line had, which
    /// on the first line means nothing at all.
    /// </summary>
    public void UnpackLine(Span<byte> line, bool first) {
      for (var x = 0; x < line.Length; ++x) {
        var value = this._Read();
        if (value < 0)
          throw new InvalidDataException("A GEM bit image ends before its picture does.");

        if (value != 256)
          line[x] = (byte)value;
        else if (first)
          line[x] = 0;
      }
    }

    private int _Read() {
      while (this._count == 0) {
        // A pattern repeats by rewinding over the bytes it was read from rather than by storing
        // them again.
        if (this._patternRepeats > 1) {
          --this._patternRepeats;
          this._count = this._patternLength;
          this._at -= this._patternLength;
          continue;
        }

        var b = this._Byte();
        switch (b) {
          case -1:
            return -1;

          case 0: {
            b = this._Byte();
            if (b < 0)
              return -1;

            // Two zeroes and a count: that many bytes stay as the previous line left them.
            if (b == 0) {
              var rows = this._Byte();
              if (rows < 0)
                return -1;

              this._count = rows + 1;
              this._value = 256;
              return this._Take();
            }

            this._patternRepeats = b;
            this._count = this._patternLength;
            this._value = -1;
            break;
          }

          // A run of literal bytes, a count of zero meaning the longest one.
          case 128:
            this._count = this._Byte();
            if (this._count < 0)
              return -1;

            if (this._count == 0)
              this._count = 256;

            this._value = -1;
            break;

          // Anything else is a solid run, the top bit choosing which of the two solid values.
          default:
            this._count = b & 127;
            this._value = b >= 128 ? 255 : 0;
            break;
        }
      }

      return this._Take();
    }

    private int _Take() {
      --this._count;

      return this._value >= 0 ? this._value : this._Byte();
    }
  }

  public static TrueColorImgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
