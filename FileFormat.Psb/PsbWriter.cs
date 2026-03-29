using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Psb;

/// <summary>Assembles PSB (Photoshop Big) file bytes from pixel data.</summary>
public static class PsbWriter {

  public static byte[] ToBytes(PsbFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Header (26 bytes)
    var header = new PsbHeader(
      (byte)'8', (byte)'B', (byte)'P', (byte)'S',
      2, // Version: PSB
      0, 0, 0, 0, 0, 0, // Reserved
      (short)file.Channels,
      file.Height,
      file.Width,
      (short)file.Depth,
      (short)file.ColorMode
    );

    Span<byte> headerBuffer = stackalloc byte[PsbHeader.StructSize];
    header.WriteTo(headerBuffer);
    ms.Write(headerBuffer);

    // Color Mode Data section (4-byte length)
    Span<byte> int32Buf = stackalloc byte[4];
    if (file.ColorMode == PsbColorMode.Indexed && file.Palette is { Length: >= 768 }) {
      BinaryPrimitives.WriteInt32BigEndian(int32Buf, 768);
      ms.Write(int32Buf);
      ms.Write(file.Palette, 0, 768);
    } else {
      BinaryPrimitives.WriteInt32BigEndian(int32Buf, 0);
      ms.Write(int32Buf);
    }

    // Image Resources section (4-byte length)
    var imageResourcesLength = file.ImageResources?.Length ?? 0;
    BinaryPrimitives.WriteInt32BigEndian(int32Buf, imageResourcesLength);
    ms.Write(int32Buf);
    if (file.ImageResources != null)
      ms.Write(file.ImageResources);

    // Layer and Mask Info section (PSB uses 8-byte length!)
    Span<byte> int64Buf = stackalloc byte[8];
    var layerMaskInfoLength = (long)(file.LayerMaskInfo?.Length ?? 0);
    BinaryPrimitives.WriteInt64BigEndian(int64Buf, layerMaskInfoLength);
    ms.Write(int64Buf);
    if (file.LayerMaskInfo != null)
      ms.Write(file.LayerMaskInfo);

    // Image Data section: compress with RLE (PackBits) using 4-byte byte counts
    var bytesPerChannel = (file.Depth + 7) / 8;
    var scanlineLength = file.Width * bytesPerChannel;
    var scanlineCount = file.Height * file.Channels;

    // Compress all scanlines first
    var compressedScanlines = new byte[scanlineCount][];
    for (var i = 0; i < scanlineCount; ++i) {
      var srcOffset = i * scanlineLength;
      var srcLength = Math.Min(scanlineLength, file.PixelData.Length - srcOffset);
      if (srcLength <= 0) {
        compressedScanlines[i] = [];
        continue;
      }

      compressedScanlines[i] = _CompressPackBits(file.PixelData.AsSpan(srcOffset, srcLength));
    }

    // Compression type
    Span<byte> compressionBuffer = stackalloc byte[2];
    BinaryPrimitives.WriteInt16BigEndian(compressionBuffer, (short)PsbCompression.Rle);
    ms.Write(compressionBuffer);

    // Byte count table (PSB: 4 bytes per entry)
    for (var i = 0; i < scanlineCount; ++i) {
      BinaryPrimitives.WriteInt32BigEndian(int32Buf, compressedScanlines[i].Length);
      ms.Write(int32Buf);
    }

    // Compressed data
    for (var i = 0; i < scanlineCount; ++i)
      ms.Write(compressedScanlines[i]);

    return ms.ToArray();
  }

  private static byte[] _CompressPackBits(ReadOnlySpan<byte> source) {
    if (source.IsEmpty)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < source.Length) {
      // Check for a run of identical bytes
      var runStart = i;
      while (i + 1 < source.Length && source[i] == source[i + 1] && i - runStart < 127)
        ++i;

      var runLength = i - runStart + 1;
      if (runLength >= 3 || (runLength == 2 && (i + 1 >= source.Length || source[i] != source[i + 1]))) {
        // Emit run: header = -(runLength - 1), then the repeated byte
        ms.WriteByte((byte)(-(runLength - 1) & 0xFF));
        ms.WriteByte(source[runStart]);
        ++i;
      } else {
        // Literal run: find extent of non-repeating bytes
        i = runStart;
        var litStart = i;
        while (i < source.Length) {
          if (i + 1 < source.Length && source[i] == source[i + 1]) {
            // Check if there are at least 3 identical bytes ahead
            var peek = i;
            while (peek + 1 < source.Length && source[peek] == source[peek + 1] && peek - i < 2)
              ++peek;
            if (peek - i >= 2)
              break;
          }

          ++i;
          if (i - litStart >= 128)
            break;
        }

        var litLength = i - litStart;
        ms.WriteByte((byte)(litLength - 1));
        ms.Write(source.Slice(litStart, litLength));
      }
    }

    return ms.ToArray();
  }
}
