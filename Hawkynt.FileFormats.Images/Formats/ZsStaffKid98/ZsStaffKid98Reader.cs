using System;
using System.IO;

namespace FileFormat.ZsStaffKid98;

/// <summary>Reads Z's Staff Kid98 pictures from bytes, streams, or file paths.</summary>
public static class ZsStaffKid98Reader {

  /// <summary>Where the offset of the picture's own header is stored.</summary>
  private const int _DIRECTORY_OFFSET = 506;

  /// <summary>The largest a run's packed planes may be.</summary>
  private const int _MAX_RUN_BYTES = 512;

  public static ZsStaffKid98File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZsStaffKid98File FromStream(Stream stream) {
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

  public static ZsStaffKid98File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 700 || !_IsStringAt(data, 0, ZsStaffKid98File.Signature))
      throw new InvalidDataException("Not a Z's Staff Kid98 picture.");

    // The header sits after a directory whose length the fixed part records, in words.
    var at = 512 + ((data[_DIRECTORY_OFFSET] | (data[_DIRECTORY_OFFSET + 1] << 8)) << 1);
    if (at + 26 > data.Length || data[at] != 0 || data[at + 1] != 0 || data[at + 2] != 0
        || data[at + 3] != 0 || data[at + 20] != 1 || data[at + 21] != 0)
      throw new InvalidDataException("A Z's Staff picture has no header where its directory ends.");

    // The dimensions are stored one less than they are, so a one-pixel picture is not an empty one.
    var width = data[at + 4] + (data[at + 5] << 8) + 1;
    var height = data[at + 6] + (data[at + 7] << 8) + 1;
    at += 24;

    var palette = new byte[16 * 3];
    if (data[at - 2] != 0 || data[at - 1] != 0) {
      if (at + 66 > data.Length)
        throw new InvalidDataException("A Z's Staff palette runs past the end of the file.");

      // Four bytes a colour, of which three are used and in the order blue, red, green.
      for (var c = 0; c < 16; ++c, at += 4) {
        palette[c * 3] = data[at + 1];
        palette[c * 3 + 1] = data[at + 2];
        palette[c * 3 + 2] = data[at];
      }
    } else {
      // No palette: the eight colours of one bit a channel, twice over, with the ninth made white
      // so that a picture drawn against it has something to be drawn against.
      for (var c = 0; c < 16; ++c) {
        palette[c * 3] = (byte)(((c >> 1) & 1) * 255);
        palette[c * 3 + 1] = (byte)(((c >> 2) & 1) * 255);
        palette[c * 3 + 2] = (byte)((c & 1) * 255);
      }

      palette[24] = palette[25] = palette[26] = 255;
    }

    var pixels = new byte[width * height];
    var stream = new _Stream(data, at, data.Length);

    // A directory of runs the decoder has no use for sits between the header and the picture.
    var skip = stream.Word();
    if (skip < 0)
      throw new InvalidDataException("A Z's Staff picture has no run list.");

    stream.At += skip << 1;

    var flags1 = new byte[1];
    var flags2 = new byte[8];
    var flags3 = new byte[64];
    var run = new byte[_MAX_RUN_BYTES];

    for (;;) {
      var length = stream.Word();
      if (length < 0)
        throw new InvalidDataException("A Z's Staff picture ends before its runs do.");

      if (length == 0)
        return new() { Width = width, Height = height, Pixels = pixels, Palette = palette };

      var x = stream.Word();
      if (x < 0 || x >= width)
        throw new InvalidDataException($"A run starts at column {x}.");

      var y = stream.Word();
      if (y < 0 || y >= height)
        throw new InvalidDataException($"A run starts on row {y}.");

      var packed = stream.Word();
      if (packed < 0)
        throw new InvalidDataException("A run has no packed length.");

      // The run's packed bytes are bounded, so a run that lies about its own size cannot read into
      // the one after it.
      stream.Limit = stream.At + packed;
      if (stream.Limit >= data.Length)
        throw new InvalidDataException("A run reaches past the end of the file.");

      var size = stream.Word();
      if (size > _MAX_RUN_BYTES || (size & 3) != 0 || size << 1 < length)
        throw new InvalidDataException($"A run of {length} pixels does not fit in {size} bytes.");

      var target = y * width + x;
      if (target + length > pixels.Length)
        throw new InvalidDataException("A run reaches past the end of the picture.");

      var first = stream.Byte();
      flags1[0] = (byte)(first < 0 ? 0 : first);
      stream.Unpack(flags1, flags2, 8);
      stream.Unpack(flags2, flags3, 64);
      stream.Unpack(flags3, run, size);
      stream.Limit = data.Length;

      // Two passes of differencing, against the previous byte and the one two back — which is what
      // turns a dither into a run of zeroes.
      for (var i = 1; i < size; ++i)
        run[i] ^= run[i - 1];

      for (var i = 2; i < size; ++i)
        run[i] ^= run[i - 2];

      // The four planes sit one after another within the run.
      var plane = size >> 2;
      for (var i = 0; i < length; ++i) {
        var bit = ~i & 7;
        var index = (((run[i >> 3] >> bit) & 1) << 3)
                    | (((run[plane + (i >> 3)] >> bit) & 1) << 2)
                    | (((run[plane * 2 + (i >> 3)] >> bit) & 1) << 1)
                    | ((run[plane * 3 + (i >> 3)] >> bit) & 1);

        pixels[target + i] = (byte)index;
      }
    }
  }

  /// <summary>
  /// The stream a run is read from, which can be limited to the run's own bytes and then released.
  /// </summary>
  private ref struct _Stream {

    private readonly ReadOnlySpan<byte> _data;

    public _Stream(ReadOnlySpan<byte> data, int at, int limit) {
      this._data = data;
      this.At = at;
      this.Limit = limit;
    }

    public int At;
    public int Limit;

    public int Byte() => this.At < this.Limit ? this._data[this.At++] : -1;

    public int Word() {
      if (this.At + 1 >= this.Limit)
        return -1;

      var value = this._data[this.At] | (this._data[this.At + 1] << 8);
      this.At += 2;

      return value;
    }

    /// <summary>
    /// Fills a block, taking a byte from the stream for each set flag bit and a zero for each
    /// clear one. Running out is not an error: the rest of the block is simply zero.
    /// </summary>
    public void Unpack(ReadOnlySpan<byte> flags, Span<byte> target, int count) {
      for (var i = 0; i < count; ++i) {
        var present = ((flags[i >> 3] >> (~i & 7)) & 1) != 0;
        var value = present ? this.Byte() : 0;
        target[i] = (byte)(value < 0 ? 0 : value);
      }
    }
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

  public static ZsStaffKid98File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
