using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl;

/// <summary>Assembles JPEG XL file bytes from pixel data.</summary>
public static class JpegXlWriter {

  public static byte[] ToBytes(JpegXlFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.ComponentCount, file.Brand);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int componentCount, string brand) {
    // Encode pixel data using modular codec
    byte[] encodedPayload;
    if (pixelData.Length > 0 && width > 0 && height > 0) {
      var frameData = JxlFrameEncoder.EncodeFrame(pixelData, width, height, componentCount, 8);
      // Prefix with 0x4D ('M') marker to indicate modular codec encoding
      encodedPayload = new byte[1 + frameData.Length];
      encodedPayload[0] = 0x4D;
      Array.Copy(frameData, 0, encodedPayload, 1, frameData.Length);
    } else
      encodedPayload = [];

    // Build codestream: FF 0A + SizeHeader + componentCount byte + encoded payload
    var sizeHeaderBytes = JpegXlSizeHeader.Encode(width, height);
    var codestreamSize = 2 + sizeHeaderBytes.Length + 1 + encodedPayload.Length;
    var codestream = new byte[codestreamSize];
    codestream[0] = 0xFF;
    codestream[1] = 0x0A;
    sizeHeaderBytes.AsSpan(0, sizeHeaderBytes.Length).CopyTo(codestream.AsSpan(2));
    codestream[2 + sizeHeaderBytes.Length] = (byte)componentCount;
    if (encodedPayload.Length > 0)
      encodedPayload.AsSpan(0, encodedPayload.Length).CopyTo(codestream.AsSpan(2 + sizeHeaderBytes.Length + 1));

    // Build ISOBMFF container: ftyp box + jxlc box
    var ftypPayload = _BuildFtypPayload(brand);
    var ftypBoxSize = 8 + ftypPayload.Length;
    var jxlcBoxSize = 8 + codestreamSize;
    var totalSize = ftypBoxSize + jxlcBoxSize;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // ftyp box
    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)ftypBoxSize);
    span[4] = (byte)'f';
    span[5] = (byte)'t';
    span[6] = (byte)'y';
    span[7] = (byte)'p';
    ftypPayload.AsSpan(0, ftypPayload.Length).CopyTo(result.AsSpan(8));

    // jxlc box
    var jxlcOffset = ftypBoxSize;
    BinaryPrimitives.WriteUInt32BigEndian(span.Slice(jxlcOffset), (uint)jxlcBoxSize);
    span[jxlcOffset + 4] = (byte)'j';
    span[jxlcOffset + 5] = (byte)'x';
    span[jxlcOffset + 6] = (byte)'l';
    span[jxlcOffset + 7] = (byte)'c';
    codestream.AsSpan(0, codestreamSize).CopyTo(result.AsSpan(jxlcOffset + 8));

    return result;
  }

  private static byte[] _BuildFtypPayload(string brand) {
    // ftyp payload: brand (4 bytes) + minor_version (4 bytes) + compatible_brands (brand repeated)
    var brandBytes = new byte[4];
    for (var i = 0; i < 4 && i < brand.Length; ++i)
      brandBytes[i] = (byte)brand[i];

    // brand + minor_version(0) + one compatible brand
    var payload = new byte[12];
    brandBytes.AsSpan(0, 4).CopyTo(payload.AsSpan(0));
    // minor_version = 0 (4 bytes, already zero)
    brandBytes.AsSpan(0, 4).CopyTo(payload.AsSpan(8));
    return payload;
  }
}
