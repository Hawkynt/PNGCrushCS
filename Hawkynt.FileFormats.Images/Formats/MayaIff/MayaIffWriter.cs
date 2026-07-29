using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.MayaIff;

/// <summary>Assembles Maya IFF file bytes from pixel data.</summary>
public static class MayaIffWriter {

  public static byte[] ToBytes(MayaIffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelData = file.PixelData;
    var pixelChunkTag = file.HasAlpha ? "RGBA"u8 : Encoding.ASCII.GetBytes("RGB ");
    var pixelDataSize = pixelData.Length;
    var pixelPaddedSize = (pixelDataSize + 3) & ~3;
    var tbhdPaddedSize = (MayaIffTbhdHeader.StructSize + 3) & ~3; // 32 is already 4-byte aligned

    // FOR4 header: 4 (FOR4) + 4 (total size) + 4 (CIMG)
    // TBHD chunk: 4 (tag) + 4 (size) + 32 (data)
    // Pixel chunk: 4 (tag) + 4 (size) + paddedPixelSize
    var bodySize = 4 /*CIMG*/ + (8 + tbhdPaddedSize) + (8 + pixelPaddedSize);
    var totalSize = 8 /*FOR4+size*/ + bodySize;

    var result = new byte[totalSize];
    var span = result.AsSpan();
    var offset = 0;

    // FOR4 magic
    "FOR4"u8.CopyTo(span[offset..]);
    offset += 4;

    // Total body size (everything after the 8-byte FOR4+size header)
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)(bodySize));
    offset += 4;

    // CIMG form type
    "CIMG"u8.CopyTo(span[offset..]);
    offset += 4;

    // TBHD chunk tag
    "TBHD"u8.CopyTo(span[offset..]);
    offset += 4;

    // TBHD chunk size
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], MayaIffTbhdHeader.StructSize);
    offset += 4;

    // TBHD data via struct serialization
    var tbhd = new MayaIffTbhdHeader(
      Width: (uint)file.Width,
      Height: (uint)file.Height,
      Prnum: 1,
      Prden: 1,
      Flags: file.HasAlpha ? 3u : 0u,
      Bytes: 1,
      Tiles: 1,
      Compression: 0
    );
    tbhd.WriteTo(span[offset..]);
    offset += MayaIffTbhdHeader.StructSize;

    // Pixel data chunk tag
    pixelChunkTag.CopyTo(span[offset..]);
    offset += 4;

    // Pixel data chunk size
    BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)pixelDataSize);
    offset += 4;

    // Pixel data
    pixelData.AsSpan().CopyTo(span[offset..]);

    return result;
  }
}
