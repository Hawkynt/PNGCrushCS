using System;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Illustrator;

/// <summary>Writes an Illustrator-6-compatible native AI document containing one embedded RGB raster.</summary>
/// <remarks>
/// The file uses Adobe's documented <c>XI</c> revisable raster object rather than generic PostScript
/// image syntax. It identifies itself as AI file-format 2.0 (Illustrator 6.0), declares the artboard,
/// and stores the RGB samples in the specification's percent-prefixed ASCII-hex form. That is what
/// makes it an Illustrator file and not a PostScript file with an Illustrator extension: the raster
/// stays an object Illustrator can select and edit rather than marks on a page.
/// <para/>
/// Choosing that dialect used to mean choosing to be unreadable by anything else, because <c>XI</c>
/// is defined nowhere in the language and the file defined it nowhere either. An Illustrator file
/// does not have to make that trade and a real one does not make it — it carries the procedure set
/// its private operators come from, in its prolog, and so runs anywhere. This does the same, for the
/// one operator it uses; see <see cref="AiRasterProcSet"/>.
/// <para/>
/// The artwork is a point to the sample, as in the EPS, PostScript and PDF writers. It was three
/// quarters of that, which put the artboard at a size nothing in the file accounted for.
/// </remarks>
public static class AiWriter {

  /// <summary>One image sample to the point, which is the resolution a PostScript device defaults to.</summary>
  private const double _PointsPerPixel = 1.0;
  private const int _HexBytesPerLine = 32;

  public static byte[] ToBytes(AiFile file) {
    var image = file.Raster ?? throw new ArgumentException("An Illustrator file needs raster artwork to write.", nameof(file));
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    if (rgb.Width <= 0 || rgb.Height <= 0)
      throw new ArgumentException("Illustrator artwork needs positive dimensions.", nameof(file));

    var widthPoints = rgb.Width * _PointsPerPixel;
    var heightPoints = rgb.Height * _PointsPerPixel;

    using var output = new MemoryStream();
    using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };

    writer.WriteLine("%!PS-Adobe-3.0");
    writer.WriteLine("%%Creator: Adobe Illustrator(TM) 6.0 compatible; PNGCrushCS");
    writer.WriteLine("%%Title: (PNGCrushCS raster artwork)");
    writer.WriteLine($"%%BoundingBox: 0 0 {Math.Ceiling(widthPoints).ToString(CultureInfo.InvariantCulture)} {Math.Ceiling(heightPoints).ToString(CultureInfo.InvariantCulture)}");
    writer.WriteLine($"%%HiResBoundingBox: 0 0 {_Number(widthPoints)} {_Number(heightPoints)}");
    writer.WriteLine("%AI5_FileFormat 2.0");
    writer.WriteLine($"%AI5_ArtSize: {_Number(heightPoints)} {_Number(widthPoints)}");
    writer.WriteLine("%AI5_RulerUnits: 2");
    writer.WriteLine("%AI5_TargetResolution: 800");
    writer.WriteLine("%AI5_NumLayers: 0");
    writer.WriteLine("%%EndComments");
    writer.WriteLine("%%BeginProlog");
    writer.WriteLine($"%%BeginResource: procset {AiRasterProcSet.Name}");
    writer.Write(AiRasterProcSet.Definition);
    writer.WriteLine("%%EndResource");
    writer.WriteLine("%%EndProlog");
    writer.WriteLine("%%BeginSetup");

    // An .ai document is a page of its own rather than artwork pasted onto somebody else's, so it
    // says how big that page is. Without this the artboard is stated only in a comment and the
    // drawing arrives in the corner of whatever medium the interpreter happens to default to.
    writer.WriteLine($"<< /PageSize [{_Number(widthPoints)} {_Number(heightPoints)}] >> setpagedevice");
    writer.WriteLine("%%EndSetup");
    writer.WriteLine("%AI5_File:");
    writer.WriteLine("%AI5_BeginRaster");
    writer.WriteLine(
      $"[ {_Number(_PointsPerPixel)} 0 0 {_Number(_PointsPerPixel)} 0 0 ] " +
      $"0 0 {rgb.Width} {rgb.Height} {rgb.Width} {rgb.Height} 8 3 0 0 0 0 XI");

    var data = rgb.PixelData;
    for (var offset = 0; offset < data.Length; offset += _HexBytesPerLine) {
      writer.Write('%');
      var end = Math.Min(data.Length, offset + _HexBytesPerLine);
      for (var i = offset; i < end; ++i)
        writer.Write(data[i].ToString("X2", CultureInfo.InvariantCulture));
      writer.WriteLine();
    }

    writer.WriteLine("%AI5_EndRaster");
    writer.WriteLine("showpage");
    writer.WriteLine("%%Trailer");
    writer.WriteLine("%%EOF");
    writer.Flush();
    return output.ToArray();
  }

  private static string _Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
