using System;
using System.Buffers.Binary;

namespace FileFormat.Psp;

/// <summary>Assembles Paint Shop Pro file bytes from pixel data.</summary>
public static class PspWriter {

  public static byte[] ToBytes(PspFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.BitDepth, file.MajorVersion, file.MinorVersion);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int bitDepth, ushort majorVersion, ushort minorVersion) {
    var generalAttributesData = _BuildGeneralAttributes(width, height, bitDepth);
    var compositeData = _BuildCompositeData(pixelData, width, height);

    // File = magic(32) + version header(4) + general attributes block + composite image block
    var generalBlockSize = 10 + generalAttributesData.Length; // block header(10) + data
    var compositeBlockSize = 10 + compositeData.Length; // block header(10) + data

    var totalSize = 32 + 4 + generalBlockSize + compositeBlockSize;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Write magic
    PspFile.Magic.CopyTo(span);
    var offset = 32;

    // Write version header
    BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], majorVersion);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 2)..], minorVersion);
    offset += 4;

    // Write General Image Attributes block
    offset = _WriteBlock(result, offset, PspFile.BlockIdGeneralAttributes, generalAttributesData);

    // Write Composite Image block
    _WriteBlock(result, offset, PspFile.BlockIdCompositeImage, compositeData);

    return result;
  }

  private static int _WriteBlock(byte[] result, int offset, ushort blockId, byte[] blockData) {
    var span = result.AsSpan(offset);
    var totalBlockLength = 10 + blockData.Length; // header(10) + data

    BinaryPrimitives.WriteUInt16LittleEndian(span, blockId);
    BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)blockData.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(span[6..], (uint)totalBlockLength);

    blockData.AsSpan(0, blockData.Length).CopyTo(result.AsSpan(offset + 10));
    return offset + totalBlockLength;
  }

  private static byte[] _BuildGeneralAttributes(int width, int height, int bitDepth) {
    // width(4) + height(4) + resolution(8) + metric(1) + compression(2) + bitDepth(2) + planeCount(2) + colorCount(4) = 27 bytes
    var data = new byte[27];
    var span = data.AsSpan();

    BinaryPrimitives.WriteInt32LittleEndian(span, width);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], height);
    BinaryPrimitives.WriteDoubleLittleEndian(span[8..], 72.0); // default 72 DPI
    data[16] = 0; // metric: pixels per inch
    BinaryPrimitives.WriteUInt16LittleEndian(span[17..], 0); // compression: none
    BinaryPrimitives.WriteUInt16LittleEndian(span[19..], (ushort)bitDepth);
    BinaryPrimitives.WriteUInt16LittleEndian(span[21..], 1); // plane count
    BinaryPrimitives.WriteUInt32LittleEndian(span[23..], bitDepth == 24 ? 16777216u : 256u); // color count

    return data;
  }

  private static byte[] _BuildCompositeData(byte[] pixelData, int width, int height) {
    var expectedSize = width * height * 3;
    var data = new byte[expectedSize];
    var copyLen = Math.Min(expectedSize, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(data.AsSpan(0));
    return data;
  }
}
