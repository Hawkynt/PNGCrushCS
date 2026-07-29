using System;
using System.Text;
using FileFormat.Tiff;

namespace FileFormat.Eps;

/// <summary>Assembles EPS file bytes with an embedded TIFF preview.</summary>
public static class EpsWriter {

  public static byte[] ToBytes(EpsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    // Build the PS section
    var ps = Encoding.ASCII.GetBytes(
      $"%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 {width} {height}\n%%EndComments\nshowpage\n%%EOF\n"
    );

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
