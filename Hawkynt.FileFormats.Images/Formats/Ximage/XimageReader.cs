using System;
using System.IO;

namespace FileFormat.Ximage;

/// <summary>Reads Ximage pictures (.xim) from bytes, streams, or file paths.</summary>
public static class XimageReader {

  /// <summary>Where each of the header's decimal fields stands, and how many characters it takes.</summary>
  private const int _VersionAt = 0, _VersionLength = 8;
  private const int _HeaderSizeAt = 8, _HeaderSizeLength = 8;
  private const int _WidthAt = 16, _WidthLength = 8;
  private const int _HeightAt = 24, _HeightLength = 8;
  private const int _ColourCountAt = 32, _ColourCountLength = 8;
  private const int _PlanesAt = 40, _PlanesLength = 3;
  private const int _DepthAt = 52, _DepthLength = 4;
  private const int _AlphaAt = 56, _AlphaLength = 4;
  private const int _RunLengthCodedAt = 60, _RunLengthCodedLength = 4;

  public static XimageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ximage picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XimageFile FromStream(Stream stream) {
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

  public static XimageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static XimageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= XimageFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an Ximage picture (the header is {XimageFile.HeaderSize} bytes and the file has {data.Length}).");

    if (_Field(data, _VersionAt, _VersionLength) != XimageFile.Version)
      throw new InvalidDataException("Not an Ximage picture: its first field is not the version 3 this reads.");

    if (_Field(data, _HeaderSizeAt, _HeaderSizeLength) != XimageFile.HeaderSize)
      throw new InvalidDataException($"An Ximage picture states a header of {XimageFile.HeaderSize} bytes and this one states something else.");

    var width = _Field(data, _WidthAt, _WidthLength);
    var height = _Field(data, _HeightAt, _HeightLength);
    if (width is < 1 or > XimageFile.MaximumSide || height is < 1 or > XimageFile.MaximumSide)
      throw new InvalidDataException($"Invalid Ximage dimensions: {width}x{height}.");

    var planes = _Field(data, _PlanesAt, _PlanesLength);
    if (planes is not (1 or 3))
      throw new InvalidDataException($"An Ximage picture has one plane or three and this one states {planes}.");

    // XnView's reader also lets the field say 0, 1 or 24, but every one of those paths ends up
    // reading a plane of one byte a sample all the same. Only the eight it states outright was
    // checked against it, so only that is read here rather than guessed at.
    var depth = _Field(data, _DepthAt, _DepthLength);
    if (depth != 8)
      throw new InvalidDataException($"An Ximage plane of {depth} bits has no reading here; only eight does.");

    // With an alpha channel and three colour planes XnView's reader takes a different path through
    // the body, so the planes are not simply the three. Nothing here has seen one, so it is refused
    // rather than read as though the flag were not set — which would draw the alpha as blue.
    if (_Field(data, _AlphaAt, _AlphaLength) != 0)
      throw new InvalidDataException("An Ximage picture with an alpha channel has no reading here.");

    var coded = _Field(data, _RunLengthCodedAt, _RunLengthCodedLength) != 0;
    var colours = _Field(data, _ColourCountAt, _ColourCountLength);

    // What the body could hold at best: one byte a sample uncompressed, or two bytes a row coded.
    // Without this a header stating sixteen thousand squared would ask for three quarters of a
    // gigabyte before reading its first run.
    var body = data[XimageFile.HeaderSize..];
    var least = coded ? (long)planes * height * 2 : (long)planes * width * height;
    if (body.Length < least)
      throw new InvalidDataException($"An Ximage picture of {width}x{height} in {planes} plane(s) needs at least {least} bytes after its header and the file has {body.Length}.");

    var palette = new byte[XimageFile.PaletteEntries * 3];
    data.Slice(XimageFile.PaletteOffset, palette.Length).CopyTo(palette);

    var count = width * height;
    var at = 0;
    var result = new byte[planes][];

    for (var p = 0; p < planes; ++p) {
      var plane = new byte[count];
      for (var y = 0; y < height; ++y) {
        var row = plane.AsSpan(y * width, width);
        if (!coded) {
          if (at + width > body.Length)
            throw new InvalidDataException($"An Ximage picture of {width}x{height} in {planes} plane(s) needs more than the {data.Length} bytes the file has.");

          body.Slice(at, width).CopyTo(row);
          at += width;
          continue;
        }

        var written = 0;
        while (written < width) {
          if (at + 1 >= body.Length)
            throw new InvalidDataException("An Ximage run ends before the row it was filling does.");

          var run = body[at] + 1;
          var value = body[at + 1];
          at += 2;
          if (run > width - written)
            run = width - written;

          row.Slice(written, run).Fill(value);
          written += run;
        }
      }

      result[p] = plane;
    }

    return new() {
      Width = width,
      Height = height,
      Planes = planes,
      HasPalette = planes == 1 && colours > 0,
      PlaneData = result,
      Palette = palette,
    };
  }

  /// <summary>Reads one of the header's fixed-width decimal fields.</summary>
  /// <remarks>
  /// The fields are text with no separators and no stated justification, so what is read is the
  /// first run of digits inside the field, which is what a decimal conversion of the field would
  /// find whichever end it was padded from.
  /// </remarks>
  private static int _Field(ReadOnlySpan<byte> data, int at, int length) {
    var value = 0;
    var seen = false;
    for (var i = at; i < at + length; ++i) {
      var c = data[i];
      if (c is >= (byte)'0' and <= (byte)'9') {
        if (value > (int.MaxValue - 9) / 10)
          return -1;

        value = value * 10 + (c - '0');
        seen = true;
        continue;
      }

      if (seen)
        break;

      if (c is not ((byte)' ' or 0 or (byte)'\t'))
        return -1;
    }

    return seen ? value : -1;
  }
}
