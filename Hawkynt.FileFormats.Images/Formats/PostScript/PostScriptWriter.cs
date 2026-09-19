using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PostScript;

/// <summary>Writes a standards-valid Level-1 PostScript program containing one RGB raster.</summary>
/// <remarks>
/// A raster carries no physical size, so writing it into a page description language means choosing
/// one, and the choice here is a point to the sample — the same one the EPS and PDF writers make and
/// the one every interpreter's default resolution agrees with. It used to be three quarters of that,
/// a pixel at ninety-six to the inch, which is a defensible size for a screen picture and a poor one
/// here: it made the page a size nothing in the file explained, and the only reader that got the
/// picture back at its original size was this package's own renderer, which assumes the same ninety-six.
/// <para/>
/// The page itself is stated twice over, because a PostScript program that states it once states it
/// to nobody. <c>%%BoundingBox</c> is what a DSC-aware cropper reads and it must be whole numbers;
/// <c>setpagedevice</c> is what the interpreter reads, and without it the medium is whatever the
/// interpreter defaults to — which is how a 320 by 200 picture used to arrive in the corner of a
/// sheet of A4.
/// </remarks>
public static class PostScriptWriter {

  /// <summary>One image sample to the point, which is the resolution a PostScript device defaults to.</summary>
  private const double _PointsPerPixel = 1.0;

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

    var header = new StringBuilder(256);
    header.Append("%!PS-Adobe-3.0\n");
    header.Append("%%Creator: Hawkynt.FileFormats.Images\n");
    header.Append("%%Pages: 1\n");
    header.Append("%%BoundingBox: 0 0 ")
      .Append(PostScriptRaster.Whole(widthPoints)).Append(' ').Append(PostScriptRaster.Whole(heightPoints)).Append('\n');
    header.Append("%%HiResBoundingBox: 0 0 ")
      .Append(PostScriptRaster.Number(widthPoints)).Append(' ').Append(PostScriptRaster.Number(heightPoints)).Append('\n');
    header.Append("%%EndComments\n");
    header.Append("%%BeginSetup\n");
    header.Append("<< /PageSize [").Append(PostScriptRaster.Number(widthPoints)).Append(' ')
      .Append(PostScriptRaster.Number(heightPoints)).Append("] >> setpagedevice\n");
    header.Append("%%EndSetup\n");
    header.Append("%%Page: 1 1\n");

    var prefix = Encoding.ASCII.GetBytes(header.ToString());
    var raster = PostScriptRaster.Draw(rgb.PixelData, image.Width, image.Height, widthPoints, heightPoints);
    var suffix = Encoding.ASCII.GetBytes("showpage\n%%Trailer\n%%EOF\n");

    var output = new byte[checked(prefix.Length + raster.Length + suffix.Length)];
    prefix.CopyTo(output, 0);
    raster.CopyTo(output, prefix.Length);
    suffix.CopyTo(output, prefix.Length + raster.Length);

    var comments = PostScriptStructure.Read(output, 0, output.Length);
    return new() { Data = output, Start = 0, End = output.Length, Comments = comments };
  }
}
