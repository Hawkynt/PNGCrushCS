using System;
using System.Buffers.Binary;
using FileFormat.Jpeg;

namespace FileFormat.TilePic;

/// <summary>
/// Writes a valid one-layer TilePic. The format permits a single layer with no scale requirement;
/// using the full image as that layer keeps arbitrary-image writing standards-compatible without
/// inventing a resampling policy for optional pyramid thumbnails.
/// </summary>
public static class TilePicWriter {

  public static byte[] ToBytes(TilePicFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException("TilePic dimensions must be positive.", nameof(file));
    var expected = checked(file.Width * file.Height * 3);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"TilePic needs {expected} RGB bytes.", nameof(file));

    var raw = new FileFormat.Core.RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = file.PixelData.AsSpan(0, expected).ToArray(),
    };
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(raw));

    const int tileCount = 1;
    const int layerCount = 1;
    const int scale = 1;
    const int attributeBytes = 0;
    var indexBytes = (tileCount + 1) * TilePicFile.IndexEntrySize;
    var tileStart = TilePicFile.HeaderSize + indexBytes;
    var tileEnd = checked(tileStart + jpeg.Length);
    var output = new byte[tileEnd];

    TilePicFile.Signature.CopyTo(output);
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4, 4), TilePicFile.HeaderSize);
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8, 4), checked((uint)file.Width));
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(12, 4), checked((uint)file.Height));
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(16, 4), checked((uint)file.Width));
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(20, 4), checked((uint)file.Height));
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(24, 4), tileCount);
    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(28, 2), layerCount);
    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(30, 2), scale);
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(32, 4), attributeBytes);
    // Header bytes 36..39 are reserved by the historical layout and remain zero.
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(TilePicFile.HeaderSize, 4), checked((uint)tileStart));
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(TilePicFile.HeaderSize + 4, 4), checked((uint)tileEnd));
    jpeg.CopyTo(output, tileStart);
    return output;
  }
}
