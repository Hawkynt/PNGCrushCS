using System;
using System.IO;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl;

/// <summary>Encodes JPEG XL as a standards-conformant bare codestream.</summary>
/// <remarks>
/// Bare <c>FF 0A</c> codestreams and the ISO BMFF <c>jxlc</c>/<c>jxlp</c> container are equally valid
/// JPEG XL files. Writing the bare form avoids inventing container metadata and keeps the encoder
/// focused on the normative modular pixel bitstream.
/// </remarks>
public static class JpegXlWriter {

  public static byte[] ToBytes(JpegXlFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData ?? [], file.Width, file.Height, file.ComponentCount, file.Brand);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int componentCount, string? brand = null) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (componentCount is < 1 or > 4)
      throw new InvalidDataException($"JPEG XL fast-lossless writer supports 1..4 8-bit channels; got {componentCount}.");

    return JxlFastLosslessEncoder.Encode(pixelData, width, height, componentCount);
  }
}
