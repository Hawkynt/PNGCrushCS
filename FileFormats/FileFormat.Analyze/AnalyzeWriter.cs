using System;
using System.Buffers.Binary;

namespace FileFormat.Analyze;

/// <summary>Assembles Analyze 7.5 file bytes from pixel data.</summary>
public static class AnalyzeWriter {

  /// <summary>Produces concatenated header (348 bytes) + pixel data.</summary>
  public static byte[] ToBytes(AnalyzeFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file);
  }

  private static byte[] _Assemble(AnalyzeFile file) {
    var result = new byte[AnalyzeReader.HEADER_SIZE + file.PixelData.Length];
    var span = result.AsSpan();

    // offset 0: int32 sizeof_hdr = 348
    BinaryPrimitives.WriteInt32LittleEndian(span, AnalyzeReader.HEADER_SIZE);

    // offset 40: dim[0] = 3 (number of dimensions)
    BinaryPrimitives.WriteInt16LittleEndian(span[40..], 3);
    // offset 42: dim[1] = width
    BinaryPrimitives.WriteInt16LittleEndian(span[42..], (short)file.Width);
    // offset 44: dim[2] = height
    BinaryPrimitives.WriteInt16LittleEndian(span[44..], (short)file.Height);
    // offset 46: dim[3] = 1 (single slice)
    BinaryPrimitives.WriteInt16LittleEndian(span[46..], 1);

    // offset 70: int16 datatype
    BinaryPrimitives.WriteInt16LittleEndian(span[70..], (short)file.DataType);

    // offset 72: int16 bitpix
    BinaryPrimitives.WriteInt16LittleEndian(span[72..], (short)file.BitsPerPixel);

    // offset 108: float32 vox_offset = 0
    BinaryPrimitives.WriteSingleLittleEndian(span[108..], 0f);

    // Copy pixel data after header
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(AnalyzeReader.HEADER_SIZE));

    return result;
  }
}
