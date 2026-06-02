using System;
using System.Buffers.Binary;

namespace FileFormat.Cdxl;

public static class CdxlWriter {

  private const int _HEADER_SIZE = 32;

  public static byte[] ToBytes(CdxlFile file) {
    ArgumentNullException.ThrowIfNull(file.Palette);
    ArgumentNullException.ThrowIfNull(file.PixelData);

    var paletteSize = file.Palette.Length;
    var bitmapSize = file.PixelData.Length;
    const int audioSize = 0;
    var frameSize = paletteSize + bitmapSize + audioSize;
    var total = _HEADER_SIZE + frameSize;

    var buf = new byte[total];
    // file_type=1 (CDXL/standard, no audio), info=0
    buf[0] = 1;
    buf[1] = 0;
    BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8, 4), (uint)frameSize); // current_chunk_size
    BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(12, 4), 0u);              // previous_chunk_size
    BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(16, 4), 0u);              // current_frame_number
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(20, 2), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(22, 2), (ushort)file.Height);
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(24, 2), (ushort)file.BitPlanes);
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(26, 2), (ushort)paletteSize);
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(28, 2), audioSize);
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(30, 2), (ushort)frameSize);
    file.Palette.AsSpan().CopyTo(buf.AsSpan(_HEADER_SIZE, paletteSize));
    file.PixelData.AsSpan().CopyTo(buf.AsSpan(_HEADER_SIZE + paletteSize, bitmapSize));
    return buf;
  }
}
