using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pcl;

/// <summary>Pulls the raster picture out of a PCL print job.</summary>
/// <remarks>
/// The stream is walked once. Everything that is not an escape sequence is text or a font, and is
/// stepped over; the sequences that belong to raster graphics are acted on and the rest are stepped
/// over too, because a parameterised sequence states its own length and can therefore be skipped
/// without knowing what it means.
/// <para/>
/// The one thing that cannot be skipped blindly is a command whose parameter is a byte count of data
/// that follows it — <c>ESC*b#W</c>, <c>ESC*b#V</c>, <c>ESC*v#W</c> and the font and macro
/// downloads. Those bytes are not commands, and a reader that did not step over them would read a
/// downloaded font as a page of rasters.
/// </remarks>
public static class PclReader {

  /// <summary>The most rows a page may hold, which bounds what a wrong count can cost.</summary>
  private const int _MaxRows = 1 << 16;

  /// <summary>The most bytes one row may take.</summary>
  private const int _MaxRowBytes = 1 << 16;

  /// <summary>How many blank rows a single offset may skip.</summary>
  private const int _MaxOffset = 1 << 16;

  /// <summary>Black and white, which is what a printer starts in.</summary>
  private static readonly byte[] _Bilevel = [255, 255, 255, 0, 0, 0];

  /// <summary>
  /// The eight-entry device RGB palette of <c>ESC*r3U</c>: black, red, green, yellow, blue,
  /// magenta, cyan, white.
  /// </summary>
  private static readonly byte[] _DeviceRgb = [
    0, 0, 0,
    255, 0, 0,
    0, 255, 0,
    255, 255, 0,
    0, 0, 255,
    255, 0, 255,
    0, 255, 255,
    255, 255, 255
  ];

  /// <summary>
  /// The eight-entry device CMY palette of <c>ESC*r-3U</c>: white, cyan, magenta, blue, yellow,
  /// green, red, black.
  /// </summary>
  private static readonly byte[] _DeviceCmy = [
    255, 255, 255,
    0, 255, 255,
    255, 0, 255,
    0, 0, 255,
    255, 255, 0,
    0, 255, 0,
    255, 0, 0,
    0, 0, 0
  ];

  public static PclFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PCL print job not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PclFile FromStream(Stream stream) {
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

  public static PclFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PclFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("A PCL print job is too short to hold a command.");

    var job = new Job();
    _Walk(data, job);

    if (job.Rows.Count == 0)
      throw new InvalidDataException("A PCL print job with no raster graphics in it: there is nothing here that is a picture.");

    return _Assemble(job);
  }

  /// <summary>What one pass over the job accumulates.</summary>
  private sealed class Job {
    public bool InRaster;
    public bool Finished;
    public int Compression;
    public int StatedWidth = -1;
    public int StatedHeight = -1;
    public int Planes = 1;
    public byte[] Palette = _Bilevel;
    public int PaletteCount = 2;
    public int RowBytes;
    public byte[]? Seed;

    /// <summary>The planes of the row being built, in the order they were sent.</summary>
    public List<byte[]> Pending = [];

    /// <summary>Every finished row, as one index a pixel.</summary>
    public List<byte[]> Rows = [];
  }

  private static void _Walk(ReadOnlySpan<byte> data, Job job) {
    var at = 0;
    while (at < data.Length) {
      if (data[at] != PclFile.Escape) {
        ++at;
        continue;
      }

      ++at;
      if (at >= data.Length)
        break;

      var first = data[at];

      // A two-character sequence is ESC and one character from 48 to 126. ESC E is the reset, and
      // it ends a page, so a picture already gathered is the one that is kept.
      if (first is >= 48 and <= 126) {
        ++at;
        if (first != (byte)'E' || job.Rows.Count == 0)
          continue;

        // A reset ends the page, and the picture already gathered is the one the file is.
        return;
      }

      if (first is < 33 or > 47) {
        ++at;
        continue;
      }

      ++at;
      if (at >= data.Length)
        break;

      var group = data[at++];
      if (group is < 96 or > 126)
        continue;

      // The rest of the sequence is one or more value-and-terminator pairs, the lower-case
      // terminators carrying the same parameterised and group characters on to the next command.
      while (at < data.Length) {
        var negative = false;
        if (data[at] is (byte)'+' or (byte)'-') {
          negative = data[at] == (byte)'-';
          ++at;
        }

        long value = 0;
        var digits = 0;
        while (at < data.Length && data[at] is >= (byte)'0' and <= (byte)'9') {
          value = value * 10 + (data[at] - '0');
          ++at;
          if (++digits > 9)
            throw new InvalidDataException("A PCL command carries a number too long to be one.");
        }

        // A decimal part is allowed and is not used by any of these commands.
        if (at < data.Length && data[at] == (byte)'.') {
          ++at;
          while (at < data.Length && data[at] is >= (byte)'0' and <= (byte)'9')
            ++at;
        }

        if (at >= data.Length)
          return;

        var terminator = data[at++];
        var last = terminator is >= 64 and <= 94;
        if (!last && terminator is < 96 or > 126)
          break;

        var command = last ? terminator : (byte)(terminator - 32);
        var number = (int)(negative ? -value : value);
        at = _Command(data, at, job, first, group, command, number, digits > 0);
        if (job.Finished)
          return;

        if (last)
          break;
      }
    }
  }

  /// <summary>Acts on one command, and returns where the stream carries on.</summary>
  private static int _Command(ReadOnlySpan<byte> data, int at, Job job, byte parameterised, byte group, byte command, int number, bool hadNumber) {
    // Commands whose parameter is a count of bytes that follow: the bytes are data, not commands.
    var carriesData =
      (parameterised == '*' && group == 'b' && command is (byte)'W' or (byte)'V')
      || (parameterised == '*' && group == 'v' && command == 'W')
      || (parameterised == ')' && group == 's' && command == 'W')
      || (parameterised == '(' && group == 's' && command == 'W')
      || (parameterised == '&' && group == 'n' && command == 'W')
      || (parameterised == '&' && group == 'p' && command == 'X');

    var length = carriesData ? Math.Max(0, number) : 0;
    if (carriesData && at + length > data.Length)
      throw new InvalidDataException($"A PCL command states {length} bytes of data and only {data.Length - at} are left in the job.");

    var payload = carriesData ? data.Slice(at, length) : default;
    var next = at + length;

    if (parameterised != '*')
      return next;

    switch (group) {
      case (byte)'t' when command == 'R':
        return next;

      case (byte)'r':
        switch (command) {
          case (byte)'A':
            if (number is < 0 or > 3)
              throw new InvalidDataException($"ESC*r{number}A is not one of the four ways the manual starts a raster.");

            _StartRaster(job);
            return next;

          case (byte)'S':
            if (!job.InRaster)
              job.StatedWidth = number;

            return next;

          case (byte)'T':
            if (!job.InRaster)
              job.StatedHeight = number;

            return next;

          case (byte)'U':
            _SimpleColour(job, number);
            return next;

          case (byte)'B' or (byte)'C':
            _EndRaster(job);
            return next;

          default:
            return next;
        }

      case (byte)'b':
        switch (command) {
          case (byte)'M':
            if (number is 4 or 6 or 7 or 8 or 9)
              throw new InvalidDataException($"PCL compression method {number} is one this reader does not decode, and a picture decoded with the wrong method is not the picture.");

            if (number is < 0 or > 5)
              throw new InvalidDataException($"PCL compression method {number} is not one the manual defines.");

            job.Compression = number;
            return next;

          case (byte)'Y':
            _Skip(job, number);
            return next;

          case (byte)'V':
            _Transfer(job, payload, false);
            return next;

          case (byte)'W':
            _Transfer(job, payload, true);
            return next;

          default:
            return next;
        }

      case (byte)'v' when command == 'W' && hadNumber:
        throw new InvalidDataException("This job configures its own image data with ESC*v#W, whose palette comes from commands this reader does not read.");

      default:
        return next;
    }
  }

  private static void _StartRaster(Job job) {
    job.InRaster = true;
    job.Pending.Clear();

    // The manual: ending a raster zeroes the seed row. Starting one therefore begins from nothing
    // rather than from whatever the last picture on the page left behind.
    job.Seed = null;
    job.RowBytes = job.StatedWidth > 0 ? (job.StatedWidth + 7) >> 3 : 0;
  }

  private static void _EndRaster(Job job) {
    job.InRaster = false;
    job.Pending.Clear();
    job.Seed = null;

    // ESC*rC also resets the compression method to zero. Both forms release the picture, and a page
    // that has one is the page that is kept.
    job.Compression = 0;
    if (job.Rows.Count > 0)
      job.Finished = true;
  }

  private static void _SimpleColour(Job job, int number) {
    switch (number) {
      case 1:
        job.Planes = 1;
        job.Palette = _Bilevel;
        job.PaletteCount = 2;
        return;

      case 3:
        job.Planes = 3;
        job.Palette = _DeviceRgb;
        job.PaletteCount = 8;
        return;

      case -3:
        job.Planes = 3;
        job.Palette = _DeviceCmy;
        job.PaletteCount = 8;
        return;

      default:
        throw new InvalidDataException($"ESC*r{number}U is not one of the simple colour modes HP's colour manual defines.");
    }
  }

  /// <summary>Moves down the page without printing, which leaves blank rows behind.</summary>
  private static void _Skip(Job job, int number) {
    if (!job.InRaster || number <= 0)
      return;

    if (number > _MaxOffset)
      throw new InvalidDataException($"A raster Y offset of {number} rows is more than a page holds.");

    var width = job.RowBytes * 8;
    for (var i = 0; i < number && job.Rows.Count < _MaxRows; ++i)
      job.Rows.Add(new byte[width]);
  }

  /// <summary>Takes one plane of a row, and finishes the row when the last plane arrives.</summary>
  private static void _Transfer(Job job, ReadOnlySpan<byte> payload, bool endsRow) {
    // A transfer outside a raster is a job that never said a picture was starting. The manual has
    // the commands locked out there, and acting on them would build a picture out of nothing.
    if (!job.InRaster)
      throw new InvalidDataException("A raster transfer arrived before ESC*r#A said a picture was starting.");

    if (job.Compression == 5) {
      if (!endsRow)
        throw new InvalidDataException("Adaptive compression sends a whole block with ESC*b#W, not a plane at a time.");

      _Adaptive(job, payload);
      return;
    }

    job.Pending.Add(_Decode(job, job.Compression, payload));
    if (!endsRow)
      return;

    if (job.Pending.Count != job.Planes)
      throw new InvalidDataException($"A row arrived in {job.Pending.Count} plane(s) where the colour mode sends {job.Planes}.");

    _Finish(job);
  }

  /// <summary>
  /// Reads a block of rows that each carry their own method.
  /// </summary>
  /// <remarks>
  /// Three bytes lead each row: the method it used, then a count as two bytes most significant
  /// first. Methods 0 to 3 make the count a byte count of the row's data; 4 makes it a number of
  /// empty rows and 5 a number of copies of the row before.
  /// </remarks>
  private static void _Adaptive(Job job, ReadOnlySpan<byte> payload) {
    var at = 0;
    while (at < payload.Length) {
      if (at + 3 > payload.Length)
        throw new InvalidDataException("An adaptive block ends in the middle of a row's three control bytes.");

      var method = payload[at];
      var count = (payload[at + 1] << 8) | payload[at + 2];
      at += 3;

      switch (method) {
        case 4: {
          _Skip(job, count);
          continue;
        }

        case 5: {
          if (job.Seed == null)
            throw new InvalidDataException("An adaptive block repeats the row before it, and there is no row before it.");

          for (var i = 0; i < count && job.Rows.Count < _MaxRows; ++i)
            job.Rows.Add(_Spread(job, [job.Seed]));

          continue;
        }

        case <= 3: {
          if (at + count > payload.Length)
            throw new InvalidDataException($"An adaptive row states {count} bytes and the block has {payload.Length - at} left.");

          job.Pending.Clear();
          job.Pending.Add(_Decode(job, method, payload.Slice(at, count)));
          at += count;
          if (job.Planes != 1)
            throw new InvalidDataException("Adaptive compression is read here only for a single plane a row.");

          _Finish(job);
          continue;
        }

        default:
          throw new InvalidDataException($"An adaptive block names row method {method}, which the manual does not define.");
      }
    }
  }

  /// <summary>Turns the planes gathered for one row into one index a pixel.</summary>
  private static void _Finish(Job job) {
    if (job.Rows.Count >= _MaxRows)
      throw new InvalidDataException($"A page of more than {_MaxRows} rows is refused rather than read.");

    // The last plane of a row becomes the seed the next delta row is built from.
    job.Seed = job.Pending[^1];
    job.Rows.Add(_Spread(job, job.Pending));
    job.Pending = [];
  }

  /// <summary>
  /// Spreads a row's planes to one byte a pixel, plane one being the least significant bit of the
  /// index, which is what makes index one red in the device RGB palette.
  /// </summary>
  private static byte[] _Spread(Job job, IReadOnlyList<byte[]> planes) {
    var width = job.RowBytes * 8;
    var pixels = new byte[width];
    for (var plane = 0; plane < planes.Count; ++plane) {
      var bits = planes[plane];
      for (var x = 0; x < width; ++x) {
        var index = x >> 3;
        if (index >= bits.Length)
          break;

        if ((bits[index] & (0x80 >> (x & 7))) != 0)
          pixels[x] |= (byte)(1 << plane);
      }
    }

    return pixels;
  }

  /// <summary>Unpacks one plane of one row and brings it to the row length.</summary>
  private static byte[] _Decode(Job job, int method, ReadOnlySpan<byte> payload) {
    var decoded = method switch {
      0 => payload.ToArray(),
      1 => _RunLength(payload),
      2 => _PackBits(payload),
      3 => _Delta(job, payload),
      _ => throw new InvalidDataException($"PCL compression method {method} is one this reader does not decode.")
    };

    // The width is fixed for the whole picture: the first row that arrives sets it where the job
    // did not, and after that a short row is padded and a long one clipped, which is what the
    // manual says the printer does.
    if (job.RowBytes == 0) {
      if (decoded.Length > _MaxRowBytes)
        throw new InvalidDataException($"A raster row of {decoded.Length} bytes is wider than a page.");

      job.RowBytes = decoded.Length;
    }

    if (decoded.Length == job.RowBytes)
      return decoded;

    var row = new byte[job.RowBytes];
    decoded.AsSpan(0, Math.Min(decoded.Length, job.RowBytes)).CopyTo(row);

    return row;
  }

  /// <summary>Pairs of a count and a byte, the count being repetitions less one.</summary>
  private static byte[] _RunLength(ReadOnlySpan<byte> payload) {
    if ((payload.Length & 1) != 0)
      throw new InvalidDataException("A run-length row is pairs of bytes, and this one has an odd number.");

    var row = new List<byte>(payload.Length);
    for (var at = 0; at < payload.Length; at += 2) {
      var repeats = payload[at] + 1;
      if (row.Count + repeats > _MaxRowBytes)
        throw new InvalidDataException($"A run-length row runs past the {_MaxRowBytes} bytes a row may take.");

      for (var i = 0; i < repeats; ++i)
        row.Add(payload[at + 1]);
    }

    return row.ToArray();
  }

  /// <summary>
  /// The TIFF rule: a control byte from 0 to 127 is followed by that many plus one bytes taken as
  /// they are, one from -1 to -127 repeats the byte after it that many plus one times, and -128 is
  /// nothing at all.
  /// </summary>
  private static byte[] _PackBits(ReadOnlySpan<byte> payload) {
    var row = new List<byte>(payload.Length * 2);
    var at = 0;
    while (at < payload.Length) {
      var control = (sbyte)payload[at++];
      if (control == -128)
        continue;

      if (control >= 0) {
        var count = control + 1;
        if (at + count > payload.Length)
          throw new InvalidDataException($"A TIFF-packed row asks for {count} literal bytes and has {payload.Length - at}.");

        for (var i = 0; i < count; ++i)
          row.Add(payload[at + i]);

        at += count;
        continue;
      }

      if (at >= payload.Length)
        throw new InvalidDataException("A TIFF-packed row ends with a repeat that has nothing to repeat.");

      var repeats = 1 - control;
      if (row.Count + repeats > _MaxRowBytes)
        throw new InvalidDataException($"A TIFF-packed row runs past the {_MaxRowBytes} bytes a row may take.");

      var value = payload[at++];
      for (var i = 0; i < repeats; ++i)
        row.Add(value);
    }

    return row.ToArray();
  }

  /// <summary>
  /// A delta row says only what changed from the row before it.
  /// </summary>
  /// <remarks>
  /// A command byte splits into a replacement count in its top three bits, which is the number of
  /// bytes less one, and an offset in its bottom five, counted from the end of the last replacement
  /// or, for the first, from the left raster margin. An offset of 31 means the count carries on into
  /// the bytes after it: each 255 adds 255 and the first byte below 255 adds itself and ends it.
  /// </remarks>
  private static byte[] _Delta(Job job, ReadOnlySpan<byte> payload) {
    var row = job.Seed != null ? (byte[])job.Seed.Clone() : new byte[job.RowBytes];
    var length = row.Length;
    var position = 0;
    var at = 0;

    while (at < payload.Length) {
      var control = payload[at++];
      var count = (control >> 5) + 1;
      var offset = control & 0x1F;

      if (offset == 31) {
        while (at < payload.Length) {
          var more = payload[at++];
          offset += more;
          if (more != 255)
            break;
        }
      }

      position += offset;
      if (at + count > payload.Length)
        throw new InvalidDataException($"A delta row asks for {count} replacement bytes and has {payload.Length - at}.");

      // A replacement that runs past the row is a row built for a wider picture than this one.
      if (position + count > length && length > 0)
        throw new InvalidDataException($"A delta row replaces bytes {position} to {position + count - 1} of a row {length} bytes long.");

      if (length == 0) {
        if (position + count > _MaxRowBytes)
          throw new InvalidDataException($"A delta row runs past the {_MaxRowBytes} bytes a row may take.");

        Array.Resize(ref row, position + count);
        length = row.Length;
      }

      payload.Slice(at, count).CopyTo(row.AsSpan(position, count));
      at += count;
      position += count;
    }

    return row;
  }

  private static PclFile _Assemble(Job job) {
    // Where the job stated a size, that is the size: the manual has the printer pad a row that
    // arrives short and clip one that arrives long, and do the same down the page.
    var width = job.StatedWidth > 0 ? job.StatedWidth : job.RowBytes * 8;
    var height = job.StatedHeight > 0 ? job.StatedHeight : job.Rows.Count;
    if (width > _MaxRowBytes * 8 || height > _MaxRows)
      throw new InvalidDataException($"A PCL raster of {width} by {height} is larger than a page.");

    if (width < 1 || height < 1)
      throw new InvalidDataException($"A PCL raster of {width} by {height} has no picture in it.");

    var pixels = new byte[width * height];
    for (var y = 0; y < height && y < job.Rows.Count; ++y) {
      var row = job.Rows[y];
      var copy = Math.Min(width, row.Length);
      Array.Copy(row, 0, pixels, y * width, copy);
    }

    return new() {
      Width = width,
      Height = height,
      Planes = job.Planes,
      PixelData = pixels,
      Palette = job.Palette,
      PaletteCount = job.PaletteCount
    };
  }
}
