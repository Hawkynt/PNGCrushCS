using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Psd;

/// <summary>Assembles PSD file bytes from pixel data.</summary>
public static class PsdWriter {

  public static byte[] ToBytes(PsdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Header (26 bytes)
    var header = new PsdHeader(
      (byte)'8', (byte)'B', (byte)'P', (byte)'S',
      1, // Version: PSD
      0, 0, 0, 0, 0, 0, // Reserved
      (short)file.Channels,
      file.Height,
      file.Width,
      (short)file.Depth,
      (short)file.ColorMode
    );

    Span<byte> headerBuffer = stackalloc byte[PsdHeader.StructSize];
    header.WriteTo(headerBuffer);
    ms.Write(headerBuffer);

    // Color Mode Data section
    Span<byte> lengthBuffer = stackalloc byte[4];
    if (file.ColorMode == PsdColorMode.Indexed && file.Palette is { Length: >= 768 }) {
      BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, 768);
      ms.Write(lengthBuffer);
      ms.Write(file.Palette, 0, 768);
    } else {
      BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, 0);
      ms.Write(lengthBuffer);
    }

    // Image Resources section
    var imageResourcesLength = file.ImageResources?.Length ?? 0;
    BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, imageResourcesLength);
    ms.Write(lengthBuffer);
    if (file.ImageResources != null)
      ms.Write(file.ImageResources);

    // Layer and Mask Info section
    var layerMaskInfoLength = file.LayerMaskInfo?.Length ?? 0;
    BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, layerMaskInfoLength);
    ms.Write(lengthBuffer);
    if (file.LayerMaskInfo != null)
      ms.Write(file.LayerMaskInfo);

    // Image Data section: Raw compression (0) + channel-planar data
    Span<byte> compressionBuffer = stackalloc byte[2];
    BinaryPrimitives.WriteInt16BigEndian(compressionBuffer, (short)PsdCompression.Raw);
    ms.Write(compressionBuffer);
    ms.Write(file.PixelData);

    return ms.ToArray();
  }
}
