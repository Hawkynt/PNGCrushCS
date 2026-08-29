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
/// and stores the RGB samples in the specification's percent-prefixed ASCII-hex form.
/// </remarks>
public static class AiWriter {

  private const double _PointsPerPixelAt96Dpi = 72.0 / 96.0;
  private const int _HexBytesPerLine = 32;

  public static byte[] ToBytes(AiFile file) {
    var image = file.Raster ?? throw new ArgumentException("An Illustrator file needs raster artwork to write.", nameof(file));
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    if (rgb.Width <= 0 || rgb.Height <= 0)
      throw new ArgumentException("Illustrator artwork needs positive dimensions.", nameof(file));

    var widthPoints = rgb.Width * _PointsPerPixelAt96Dpi;
    var heightPoints = rgb.Height * _PointsPerPixelAt96Dpi;

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
    writer.WriteLine("%%EndProlog");
    writer.WriteLine("%AI5_File:");
    writer.WriteLine("%AI5_BeginRaster");
    writer.WriteLine(
      $"[ {_Number(_PointsPerPixelAt96Dpi)} 0 0 {_Number(_PointsPerPixelAt96Dpi)} 0 0 ] " +
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
    writer.WriteLine("%%Trailer");
    writer.WriteLine("%%EOF");
    writer.Flush();
    return output.ToArray();
  }

  private static string _Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
