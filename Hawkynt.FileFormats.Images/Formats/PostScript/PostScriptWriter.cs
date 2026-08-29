using System;
using System.Globalization;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PostScript;

/// <summary>Writes a standards-valid Level-1 PostScript program containing one RGB raster.</summary>
public static class PostScriptWriter {

  private const double _PointsPerPixel = 72.0 / 96.0;
  private static ReadOnlySpan<byte> _Hex => "0123456789ABCDEF"u8;

  public static byte[] ToBytes(PostScriptFile file) {
    if (file.Data == null || file.Data.Length < 2 || file.Data[0] != (byte)'%' || file.Data[1] != (byte)'!')
      throw new ArgumentException("A PostScript file must contain a complete %! program.", nameof(file));
    return file.Data[..];
  }

  public static PostScriptFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("PostScript image dimensions must be positive.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var widthPoints = image.Width * _PointsPerPixel;
    var heightPoints = image.Height * _PointsPerPixel;
    var lineBytes = checked(image.Width * 3);

    var header = new StringBuilder(256);
    header.AppendLine("%!PS-Adobe-3.0");
    header.AppendLine("%%Creator: Hawkynt.FileFormats.Images");
    header.AppendLine("%%Pages: 1");
    header.Append("%%HiResBoundingBox: 0 0 ")
      .Append(_Number(widthPoints)).Append(' ').AppendLine(_Number(heightPoints));
    header.AppendLine("%%EndComments");
    header.Append("/picstr ").Append(lineBytes).AppendLine(" string def");
    header.AppendLine("gsave");
    header.Append(_Number(widthPoints)).Append(' ').Append(_Number(heightPoints)).AppendLine(" scale");
    header.Append(image.Width).Append(' ').Append(image.Height).AppendLine(" 8");
    header.Append('[').Append(image.Width).Append(" 0 0 -").Append(image.Height).Append(" 0 ").Append(image.Height).AppendLine("]");
    header.AppendLine("{ currentfile picstr readhexstring pop }");
    header.AppendLine("false 3 colorimage");

    var prefix = Encoding.ASCII.GetBytes(header.ToString());
    var suffix = Encoding.ASCII.GetBytes("\ngrestore\nshowpage\n%%EOF\n");
    var dataBytes = checked(rgb.PixelData.Length * 2);
    var breaks = dataBytes == 0 ? 0 : (dataBytes - 1) / 128;
    var output = new byte[checked(prefix.Length + dataBytes + breaks + suffix.Length)];
    prefix.CopyTo(output, 0);

    var at = prefix.Length;
    var onLine = 0;
    foreach (var value in rgb.PixelData) {
      if (onLine == 128) {
        output[at++] = (byte)'\n';
        onLine = 0;
      }
      output[at++] = _Hex[value >> 4];
      output[at++] = _Hex[value & 15];
      onLine += 2;
    }
    suffix.CopyTo(output, at);

    var comments = PostScriptStructure.Read(output, 0, output.Length);
    return new() { Data = output, Start = 0, End = output.Length, Comments = comments };
  }

  private static string _Number(double value)
    => value.ToString("0.##", CultureInfo.InvariantCulture);
}
