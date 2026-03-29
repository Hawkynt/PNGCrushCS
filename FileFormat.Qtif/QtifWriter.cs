using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.Qtif;

/// <summary>Assembles QTIF (QuickTime Image) file bytes from pixel data.</summary>
public static class QtifWriter {

  /// <summary>Size of the fixed image description structure (86 bytes).</summary>
  private const int _IDSC_SIZE = 86;

  /// <summary>Codec type for uncompressed RGB: "raw " (0x72617720).</summary>
  private static readonly byte[] _RAW_CODEC = "raw "u8.ToArray();

  /// <summary>Fixed 72 dpi as 16.16 fixed-point: 0x00480000.</summary>
  private const uint _DPI_72 = 0x00480000;

  public static byte[] ToBytes(QtifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var idscAtomSize = 8 + _IDSC_SIZE;
    var idatAtomSize = 8 + pixelData.Length;
    var totalSize = idscAtomSize + idatAtomSize;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    // --- idsc atom ---
    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)idscAtomSize);
    Encoding.ASCII.GetBytes("idsc", span[4..]);

    var idsc = span[8..];

    // Image description size field
    BinaryPrimitives.WriteUInt32BigEndian(idsc, (uint)_IDSC_SIZE);

    // Codec type at offset 4
    _RAW_CODEC.CopyTo(idsc[4..]);

    // Reserved (6 bytes at offset 8) - already zeroed
    // Data ref index (uint16 at offset 14) - 0
    // Version (uint16 at offset 16) - 0
    // Revision (uint16 at offset 18) - 0
    // Vendor (4 bytes at offset 20) - zeroed

    // Temporal quality (uint32 at offset 24) - 0
    // Spatial quality (uint32 at offset 28) - 0

    // Width (uint16 at offset 32)
    BinaryPrimitives.WriteUInt16BigEndian(idsc[32..], (ushort)width);

    // Height (uint16 at offset 34)
    BinaryPrimitives.WriteUInt16BigEndian(idsc[34..], (ushort)height);

    // Horizontal resolution (fixed 16.16 at offset 36) - 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(idsc[36..], _DPI_72);

    // Vertical resolution (fixed 16.16 at offset 40) - 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(idsc[40..], _DPI_72);

    // Data size (uint32 at offset 44) - 0 (variable)
    // Frame count (uint16 at offset 48) - 1
    BinaryPrimitives.WriteUInt16BigEndian(idsc[48..], 1);

    // Compressor name (32-byte pascal string at offset 50)
    // First byte is length, rest is name padded with zeros
    idsc[50] = 3;
    Encoding.ASCII.GetBytes("raw", idsc[51..]);

    // Depth (uint16 at offset 82) - 24 for RGB
    BinaryPrimitives.WriteUInt16BigEndian(idsc[82..], 24);

    // CLUT ID (int16 at offset 84) - -1 (no color lookup table)
    BinaryPrimitives.WriteInt16BigEndian(idsc[84..], -1);

    // --- idat atom ---
    var idatOffset = idscAtomSize;
    BinaryPrimitives.WriteUInt32BigEndian(span[idatOffset..], (uint)idatAtomSize);
    Encoding.ASCII.GetBytes("idat", span[(idatOffset + 4)..]);
    pixelData.CopyTo(span[(idatOffset + 8)..]);

    return result;
  }
}
