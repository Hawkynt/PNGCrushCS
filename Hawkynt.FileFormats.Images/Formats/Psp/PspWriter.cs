using System;
using System.Buffers.Binary;

namespace FileFormat.Psp;

/// <summary>Assembles Paint Shop Pro file bytes from pixel data.</summary>
public static class PspWriter {

  public static byte[] ToBytes(PspFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelData = file.PixelData ?? Array.Empty<byte>();
    var bitDepth = file.BitDepth == 0 ? 24 : file.BitDepth;
    var majorVersion = file.MajorVersion == 0 ? (ushort)5 : file.MajorVersion;
    return Assemble(pixelData, file.Width, file.Height, bitDepth, majorVersion, file.MinorVersion);
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

  /// <summary>The four bytes every block opens with.</summary>
  private static ReadOnlySpan<byte> _BlockMarker => [0x7E, 0x42, 0x4B, 0x00];

  /// <summary>Writes one block: the marker, the identifier, the length of the data, then the data.</summary>
  /// <remarks>
  /// The marker was left out, and the identifier written where it belongs — so a real reader took
  /// the identifier out of the first two bytes of what should have been the marker. Our own reader
  /// left it out in the same place and the two agreed, which is why nothing said so.
  /// <para/>
  /// The length that follows the identifier is the data's, not the whole block's. Both were written,
  /// the second where the data belongs.
  /// </remarks>
  private static int _WriteBlock(byte[] result, int offset, ushort blockId, byte[] blockData) {
    var span = result.AsSpan(offset);

    _BlockMarker.CopyTo(span);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], blockId);
    BinaryPrimitives.WriteUInt32LittleEndian(span[6..], (uint)blockData.Length);

    blockData.AsSpan(0, blockData.Length).CopyTo(result.AsSpan(offset + 10));

    return offset + 10 + blockData.Length;
  }

  private static byte[] _BuildGeneralAttributes(int width, int height, int bitDepth) {
    // The block states the length of its own first chunk before anything else, which was missing:
    // chunk(4) + width(4) + height(4) + resolution(8) + metric(1) + compression(2) + bitDepth(2)
    // + planeCount(2) + colorCount(4).
    var data = new byte[31];
    var span = data.AsSpan();

    BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)data.Length);
    span = span[4..];
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
