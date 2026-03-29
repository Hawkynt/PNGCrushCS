using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Tiff;

namespace FileFormat.Eps;

/// <summary>Assembles EPS file bytes with an embedded TIFF preview.</summary>
public static class EpsWriter {

  private const int _HEADER_SIZE = 30;

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

    var psOffset = (uint)_HEADER_SIZE;
    var psLength = (uint)ps.Length;
    var tiffOffset = psOffset + psLength;
    var tiffLength = (uint)tiffData.Length;

    var totalSize = _HEADER_SIZE + ps.Length + tiffData.Length;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Magic
    result[0] = 0xC5;
    result[1] = 0xD0;
    result[2] = 0xD3;
    result[3] = 0xC6;

    // PS section offset + length
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], psOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(span[8..], psLength);

    // WMF preview offset + length (none)
    BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0);
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0);

    // TIFF preview offset + length
    BinaryPrimitives.WriteUInt32LittleEndian(span[20..], tiffOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(span[24..], tiffLength);

    // Checksum (0xFFFF = no checksum)
    BinaryPrimitives.WriteUInt16LittleEndian(span[28..], 0xFFFF);

    // PS section data
    ps.AsSpan(0, ps.Length).CopyTo(result.AsSpan((int)psOffset));

    // TIFF preview data
    tiffData.AsSpan(0, tiffData.Length).CopyTo(result.AsSpan((int)tiffOffset));

    return result;
  }
}
