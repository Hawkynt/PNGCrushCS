using System;
using System.IO;

namespace FileFormat.Pvr;

/// <summary>Assembles PVR file bytes from a PvrFile model.</summary>
public static class PvrWriter {

  public static byte[] ToBytes(PvrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var metadataSize = file.Metadata.Length;
    var totalSize = PvrHeader.StructSize + metadataSize + file.CompressedData.Length;

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
      file.Metadata.AsSpan(0, metadataSize).CopyTo(result.AsSpan(PvrHeader.StructSize));

    if (file.CompressedData.Length > 0)
      file.CompressedData.AsSpan(0, file.CompressedData.Length).CopyTo(result.AsSpan(PvrHeader.StructSize + metadataSize));

    return result;
  }
}
