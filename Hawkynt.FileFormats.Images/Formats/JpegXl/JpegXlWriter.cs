using System;
using System.Buffers.Binary;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl;

/// <summary>Assembles a JPEG XL file from pixel data.</summary>
/// <remarks>
/// What comes out is a lossless modular codestream — the picture's own samples,
/// each predicted from its neighbours and entropy-coded — inside the container
/// ISO/IEC 18181-2 defines. Nothing is quantised and no colour transform is
/// applied, so a file written here decodes back to the samples it was given.
/// </remarks>
public static class JpegXlWriter {

  public static byte[] ToBytes(JpegXlFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    var bits = file.BitsPerSample == 0 ? 8 : file.BitsPerSample;
    var codestream = JxlCodestreamEncoder.Encode(file.PixelData, file.Width, file.Height, file.ComponentCount, bits);
    return _Wrap(codestream, string.IsNullOrEmpty(file.Brand) ? "jxl " : file.Brand);
  }

  /// <summary>
  /// Put the codestream in the container: the signature box that says what the
  /// file is, the file-type box that says which version of it, and the box that
  /// holds the codestream whole.
  /// </summary>
  private static byte[] _Wrap(byte[] codestream, string brand) {
    var brandBytes = new byte[4];
    for (var i = 0; i < 4; ++i)
      brandBytes[i] = i < brand.Length ? (byte)brand[i] : (byte)' ';

    const int signatureBoxSize = 12;
    const int ftypBoxSize = 20;
    var codestreamBoxSize = 8 + codestream.Length;
    var result = new byte[signatureBoxSize + ftypBoxSize + codestreamBoxSize];
    var span = result.AsSpan();

    BinaryPrimitives.WriteUInt32BigEndian(span, signatureBoxSize);
    "JXL "u8.CopyTo(span[4..]);
    span[8] = 0x0D;
    span[9] = 0x0A;
    span[10] = 0x87;
    span[11] = 0x0A;

    var at = signatureBoxSize;
    BinaryPrimitives.WriteUInt32BigEndian(span[at..], ftypBoxSize);
    "ftyp"u8.CopyTo(span[(at + 4)..]);
    brandBytes.CopyTo(span[(at + 8)..]);
    // minor version zero, then the one brand the file is compatible with
    brandBytes.CopyTo(span[(at + 16)..]);

    at += ftypBoxSize;
    BinaryPrimitives.WriteUInt32BigEndian(span[at..], (uint)codestreamBoxSize);
    "jxlc"u8.CopyTo(span[(at + 4)..]);
    codestream.CopyTo(span[(at + 8)..]);
    return result;
  }
}
