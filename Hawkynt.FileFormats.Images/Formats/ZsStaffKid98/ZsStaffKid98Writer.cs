using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.ZsStaffKid98;

/// <summary>Assembles Z's Staff Kid98 (.zim) file bytes.</summary>
/// <remarks>
/// The picture is written as one run per row, split where a row is longer than a run may be. Runs
/// are what the format has instead of a screen, and a row is the longest one that costs nothing to
/// find — a program looking for the background would have to decide what the background is, and the
/// file states no such thing.
/// </remarks>
public static class ZsStaffKid98Writer {

  /// <summary>Bytes of fixed part before the directory the header follows.</summary>
  private const int _FIXED_PART = 512;

  /// <summary>Where the directory's length in words is stored.</summary>
  private const int _DIRECTORY_OFFSET = 506;

  /// <summary>Bytes the picture's own header occupies.</summary>
  private const int _HEADER_SIZE = 24;

  /// <summary>Bytes one palette entry occupies, of which three are used.</summary>
  private const int _PALETTE_ENTRY_SIZE = 4;

  /// <summary>The largest a run's packed planes may be.</summary>
  private const int _MAX_RUN_BYTES = 512;

  /// <summary>Pixels one run may cover, four planes of a byte per eight pixels each.</summary>
  private const int _MAX_RUN_PIXELS = _MAX_RUN_BYTES * 2;

  /// <summary>Bitplanes a run stores, the first of which carries the index's high bit.</summary>
  private const int _PLANES = 4;

  /// <summary>The shortest a file may be before the reader stops believing it is one.</summary>
  private const int _MIN_FILE_SIZE = 700;

  public static byte[] ToBytes(ZsStaffKid98File file) {
    int width = file.Width, height = file.Height;
    var pixels = file.Pixels ?? [];
    if (width < 1 || height < 1 || pixels.Length < width * height)
      throw new InvalidDataException($"A {width}x{height} Z's Staff picture needs {width * height} pixels, got {pixels.Length}.");

    var data = new List<byte>(new byte[_FIXED_PART]);
    for (var i = 0; i < ZsStaffKid98File.Signature.Length; ++i)
      data[i] = (byte)ZsStaffKid98File.Signature[i];

    // No directory, so the picture's header starts where the fixed part ends.
    data[_DIRECTORY_OFFSET] = 0;
    data[_DIRECTORY_OFFSET + 1] = 0;

    var header = new byte[_HEADER_SIZE];

    // The dimensions are stored one less than they are, so a one-pixel picture is not an empty one.
    header[4] = (byte)(width - 1);
    header[5] = (byte)((width - 1) >> 8);
    header[6] = (byte)(height - 1);
    header[7] = (byte)((height - 1) >> 8);
    header[20] = 1;

    // Anything but zero in the last word says a palette follows.
    header[22] = 1;
    data.AddRange(header);

    var palette = file.Palette ?? [];
    for (var color = 0; color < ZsStaffKid98File.ColorCount; ++color) {
      var entry = color * 3;

      // Three bytes a colour of the four stored, in the order blue, red, green.
      data.Add(entry + 2 < palette.Length ? palette[entry + 2] : (byte)0);
      data.Add(entry < palette.Length ? palette[entry] : (byte)0);
      data.Add(entry + 1 < palette.Length ? palette[entry + 1] : (byte)0);
      data.Add(0);
    }

    // The list of runs the decoder has no use for; there is none, so it is empty.
    data.Add(0);
    data.Add(0);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; x += _MAX_RUN_PIXELS)
        _WriteRun(data, pixels, y * width + x, Math.Min(_MAX_RUN_PIXELS, width - x), x, y);

    // A length of zero closes the list.
    data.Add(0);
    data.Add(0);

    while (data.Count < _MIN_FILE_SIZE)
      data.Add(0);

    return [.. data];
  }

  /// <summary>Writes one horizontal run: where it starts, how long it is, and its four planes.</summary>
  private static void _WriteRun(List<byte> data, ReadOnlySpan<byte> pixels, int at, int length, int x, int y) {
    var plane = (length + 7) >> 3;
    var size = plane * _PLANES;
    var run = new byte[size];

    for (var i = 0; i < length; ++i) {
      var index = pixels[at + i] & 15;
      var bit = (byte)(1 << (~i & 7));

      // The first plane carries the index's high bit and the last its low one.
      for (var p = 0; p < _PLANES; ++p)
        if ((index & (1 << (_PLANES - 1 - p))) != 0)
          run[p * plane + (i >> 3)] |= bit;
    }

    _Undifference(run);

    var flags = _Flags(run);
    var body = new List<byte> {
      (byte)size,
      (byte)(size >> 8),
      flags.First,
    };
    body.AddRange(flags.Second);
    body.AddRange(flags.Third);
    body.AddRange(flags.Values);

    _AddWord(data, length);
    _AddWord(data, x);
    _AddWord(data, y);
    _AddWord(data, body.Count);
    data.AddRange(body);
  }

  /// <summary>
  /// Undoes the two passes of differencing the reader applies, so that what is stored comes back as
  /// what was wanted.
  /// </summary>
  /// <remarks>
  /// The reader exclusive-ors each byte with the one before it and then each byte with the one two
  /// back, both times using values the same pass has already changed. Inverting that means running
  /// the passes in the opposite order and reading the untouched neighbours, which is why the second
  /// loop here goes forwards over the result of the first rather than both going the same way.
  /// </remarks>
  private static void _Undifference(Span<byte> run) {
    for (var i = run.Length; --i >= 2;)
      run[i] ^= run[i - 2];

    for (var i = run.Length; --i >= 1;)
      run[i] ^= run[i - 1];
  }

  /// <summary>
  /// Builds the three levels of flags that say which bytes are stored at all, and the bytes
  /// themselves.
  /// </summary>
  /// <remarks>
  /// A byte of bits says which of eight bytes follow, each of those says which of eight more follow,
  /// and those say which of the run's bytes follow — so a plane that is mostly empty costs a bit per
  /// eight bytes instead of a byte. Bits run from the most significant down, and a level's byte is
  /// only stored when something below it is.
  /// </remarks>
  private static (byte First, byte[] Second, byte[] Third, byte[] Values) _Flags(ReadOnlySpan<byte> run) {
    var third = new byte[64];
    var values = new List<byte>();

    for (var i = 0; i < run.Length; ++i) {
      if (run[i] == 0)
        continue;

      third[i >> 3] |= (byte)(1 << (~i & 7));
    }

    var second = new byte[8];
    for (var j = 0; j < third.Length; ++j)
      if (third[j] != 0)
        second[j >> 3] |= (byte)(1 << (~j & 7));

    byte first = 0;
    for (var k = 0; k < second.Length; ++k)
      if (second[k] != 0)
        first |= (byte)(1 << (~k & 7));

    var storedSecond = new List<byte>();
    for (var k = 0; k < second.Length; ++k)
      if (second[k] != 0)
        storedSecond.Add(second[k]);

    var storedThird = new List<byte>();
    for (var j = 0; j < third.Length; ++j)
      if (((second[j >> 3] >> (~j & 7)) & 1) != 0)
        storedThird.Add(third[j]);

    for (var i = 0; i < run.Length; ++i)
      if (((third[i >> 3] >> (~i & 7)) & 1) != 0)
        values.Add(run[i]);

    return (first, [.. storedSecond], [.. storedThird], [.. values]);
  }

  private static void _AddWord(List<byte> data, int value) {
    data.Add((byte)value);
    data.Add((byte)(value >> 8));
  }
}
