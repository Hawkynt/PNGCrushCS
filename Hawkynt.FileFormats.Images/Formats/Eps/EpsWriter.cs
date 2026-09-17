using System;
using System.Text;
using FileFormat.PostScript;
using FileFormat.Tiff;

namespace FileFormat.Eps;

/// <summary>Assembles EPS file bytes with an embedded TIFF preview.</summary>
/// <remarks>
/// An EPS is a PostScript program first and a preview second. The preview exists so a page-layout
/// application can show something without running an interpreter, and the program is what actually
/// gets printed — which is why a file whose program draws nothing is broken however good the preview
/// looks. This used to write exactly that: a bounding box, <c>showpage</c>, and eighty bytes of
/// nothing in between, with the whole picture living in the TIFF. Every tool that appeared to read
/// it was reading the preview.
/// <para/>
/// The picture is stated at one point to the sample, which is what the bounding box has always said
/// here and what the PostScript and PDF writers now agree with.
/// </remarks>
public static class EpsWriter {

  public static byte[] ToBytes(EpsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    // The PostScript section, which is the file. An EPS may not call setpagedevice — it is artwork
    // pasted onto somebody else's page, not a page of its own — so the bounding box is the only
    // statement of size it gets to make, and -dEPSCrop is what a renderer reads it with.
    var header = Encoding.ASCII.GetBytes(
      $"%!PS-Adobe-3.0 EPSF-3.0\n"
      + $"%%Creator: Hawkynt.FileFormats.Images\n"
      + $"%%BoundingBox: 0 0 {width} {height}\n"
      + $"%%HiResBoundingBox: 0 0 {width} {height}\n"
      + $"%%EndComments\n"
    );
    var raster = PostScriptRaster.Draw(pixelData ?? [], width, height, width, height);
    var trailer = Encoding.ASCII.GetBytes("showpage\n%%EOF\n");

    var ps = new byte[checked(header.Length + raster.Length + trailer.Length)];
    header.CopyTo(ps, 0);
    raster.CopyTo(ps, header.Length);
    trailer.CopyTo(ps, header.Length + raster.Length);

    // Build the TIFF preview via TiffWriter
    var tiff = new TiffFile {
      Width = width,
      Height = height,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb,
    };
    var tiffData = TiffWriter.ToBytes(tiff);

    var psOffset = (uint)EpsHeader.StructSize;
    var psLength = (uint)ps.Length;
    var tiffOffset = psOffset + psLength;
    var tiffLength = (uint)tiffData.Length;

    var totalSize = EpsHeader.StructSize + ps.Length + tiffData.Length;
    var result = new byte[totalSize];

    // Write the complete header via generated serializer
    new EpsHeader(
      Magic: EpsHeader.ExpectedMagic,
      PsOffset: psOffset,
      PsLength: psLength,
      WmfOffset: 0,
      WmfLength: 0,
      TiffOffset: tiffOffset,
      TiffLength: tiffLength,
      Checksum: 0xFFFF
    ).WriteTo(result);

    // PS section data
    ps.AsSpan().CopyTo(result.AsSpan((int)psOffset));

    // TIFF preview data
    tiffData.AsSpan().CopyTo(result.AsSpan((int)tiffOffset));

    return result;
  }
}
