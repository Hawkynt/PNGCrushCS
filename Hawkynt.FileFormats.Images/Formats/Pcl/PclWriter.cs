using System;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Pcl;

/// <summary>Writes the raster half of a PCL print job.</summary>
/// <remarks>
/// The job is the shortest one chapter 15 of the manual describes that prints a picture: a reset, the
/// raster resolution, the simple colour mode, the source width and height, <c>ESC*r0A</c> to start
/// the raster, a row at a time in TIFF packing, <c>ESC*rC</c> to end it and a reset to eject the
/// page. Nothing else — no text, no fonts, no HP-GL/2 — because none of those is part of the picture
/// the file holds.
/// <para/>
/// The width and the height are stated with <c>ESC*r#S</c> and <c>ESC*r#T</c> before the raster
/// starts, which is the only place a printer takes them: inside a raster the same two commands are
/// locked out, and a job that stated them there would print at whatever width its first row happened
/// to be.
/// <para/>
/// TIFF packing rather than unencoded, because it is the one of the two the manual defines that costs
/// nothing to get wrong in a way the other would hide: a row that packs to more than it started as is
/// still a row the printer prints. Method 2 is what <c>ESC*b2M</c> selects and what the reader here
/// and every printer since the LaserJet III decode.
/// </remarks>
public static class PclWriter {

  /// <summary>Dots an inch the raster is printed at, which is what <c>ESC*t#R</c> states.</summary>
  private const int _Resolution = 300;

  /// <summary>Simple colour: one plane of black and white, or three of device RGB.</summary>
  private const int _BilevelMode = 1, _DeviceRgbMode = 3;

  /// <summary>The TIFF rule, which is what <c>ESC*b#M</c> selects.</summary>
  private const int _TiffPacking = 2;

  public static byte[] ToBytes(PclFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException($"Invalid PCL raster size: {width}x{height}.", nameof(file));

    var planes = file.Planes;
    if (planes is not (1 or 3))
      throw new ArgumentException($"A PCL raster is sent in one plane a row or three, not {planes}.", nameof(file));

    var pixels = file.PixelData ?? new byte[width * height];
    if (pixels.Length < width * height)
      throw new ArgumentException($"A PCL raster of {width} by {height} needs {width * height} bytes and has {pixels.Length}.", nameof(file));

    using var job = new MemoryStream();
    _Escape(job, "E");
    _Command(job, '*', 't', _Resolution, 'R');
    _Command(job, '*', 'r', planes == 1 ? _BilevelMode : _DeviceRgbMode, 'U');
    _Command(job, '*', 'r', width, 'S');
    _Command(job, '*', 'r', height, 'T');
    _Command(job, '*', 'r', 0, 'A');
    _Command(job, '*', 'b', _TiffPacking, 'M');

    var rowBytes = (width + 7) >> 3;
    var plane = new byte[rowBytes];

    for (var y = 0; y < height; ++y)
      for (var p = 0; p < planes; ++p) {
        Array.Clear(plane);
        var row = y * width;
        for (var x = 0; x < width; ++x)
          if ((pixels[row + x] & (1 << p)) != 0)
            plane[x >> 3] |= (byte)(0x80 >> (x & 7));

        // Every plane but the last is handed over with V, which leaves the row open; W closes it.
        var packed = PackBits.Pack(plane);
        _Command(job, '*', 'b', packed.Length, p == planes - 1 ? 'W' : 'V');
        job.Write(packed);
      }

    _Command(job, '*', 'r', null, 'C');
    _Escape(job, "E");

    return job.ToArray();
  }

  private static void _Escape(Stream job, string body) {
    job.WriteByte(PclFile.Escape);
    job.Write(Encoding.ASCII.GetBytes(body));
  }

  private static void _Command(Stream job, char parameterised, char group, int? number, char terminator) {
    job.WriteByte(PclFile.Escape);
    job.WriteByte((byte)parameterised);
    job.WriteByte((byte)group);
    if (number != null)
      job.Write(Encoding.ASCII.GetBytes(number.Value.ToString(CultureInfo.InvariantCulture)));

    job.WriteByte((byte)terminator);
  }
}
