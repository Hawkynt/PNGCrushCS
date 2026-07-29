using System;
using System.IO;

namespace FileFormat.Pvr;

/// <summary>Assembles PVR file bytes from a PvrFile model.</summary>
public static class PvrWriter {

  public static byte[] ToBytes(PvrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var metadataSize = (file.Metadata ?? Array.Empty<byte>()).Length;
    var totalSize = PvrHeader.StructSize + metadataSize + (file.CompressedData ?? Array.Empty<byte>()).Length;

    var header = new PvrHeader(
      PvrHeader.Magic,
      file.Flags,
      (ulong)file.PixelFormat,
      (uint)file.ColorSpace,
      file.ChannelType,
      (uint)file.Height,
      (uint)file.Width,
      (uint)file.Depth,
      (uint)file.Surfaces,
      (uint)file.Faces,
      (uint)file.MipmapCount,
      (uint)metadataSize
    );

    var result = new byte[totalSize];
    header.WriteTo(result);

    if (metadataSize > 0)
      (file.Metadata ?? Array.Empty<byte>()).AsSpan(0, metadataSize).CopyTo(result.AsSpan(PvrHeader.StructSize));

    if ((file.CompressedData ?? Array.Empty<byte>()).Length > 0)
      (file.CompressedData ?? Array.Empty<byte>()).AsSpan(0, (file.CompressedData ?? Array.Empty<byte>()).Length).CopyTo(result.AsSpan(PvrHeader.StructSize + metadataSize));

    return result;
  }
}
