using System;
using System.IO;

namespace FileFormat.Vtf;

/// <summary>Assembles VTF file bytes from texture data.</summary>
public static class VtfWriter {

  public static byte[] ToBytes(VtfFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    var lowResFormat = file.ThumbnailData != null ? VtfFormat.Dxt1 : VtfFormat.None;
    byte lowResWidth = 0;
    byte lowResHeight = 0;

    if (file.ThumbnailData != null) {
      // Estimate thumbnail dimensions from DXT1 data size: blocks * 8 bytes
      // For simplicity, use a small fixed size or derive from data length
      var thumbBlocks = file.ThumbnailData.Length / 8;
      if (thumbBlocks > 0) {
        lowResWidth = (byte)Math.Min(255, Math.Max(4, (int)Math.Sqrt(thumbBlocks) * 4));
        lowResHeight = lowResWidth;
      }
    }

    var headerSize = VtfHeader.StructSize;
    var header = new VtfHeader(
      Sig0: (byte)'V',
      Sig1: (byte)'T',
      Sig2: (byte)'F',
      Sig3: 0,
      VersionMajor: file.VersionMajor > 0 ? file.VersionMajor : 7,
      VersionMinor: file.VersionMinor > 0 ? file.VersionMinor : 2,
      HeaderSize: headerSize,
      Width: (short)file.Width,
      Height: (short)file.Height,
      Flags: (int)file.Flags,
      Frames: (short)file.Frames,
      FirstFrame: 0,
      Padding0: 0,
      ReflectivityR: 0f,
      ReflectivityG: 0f,
      ReflectivityB: 0f,
      Padding1: 0,
      BumpmapScale: 1.0f,
      HighResFormat: (int)file.Format,
      MipmapCount: (byte)file.MipmapCount,
      LowResFormat: (int)lowResFormat,
      LowResWidth: lowResWidth,
      LowResHeight: lowResHeight,
      Padding2: 0
    );

    var headerBytes = new byte[VtfHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Write thumbnail data
    if (file.ThumbnailData != null)
      ms.Write(file.ThumbnailData);

    // Write mipmap surfaces smallest-to-largest
    // Surfaces should already be ordered smallest-to-largest in the list
    foreach (var surface in file.Surfaces)
      ms.Write(surface.Data);

    return ms.ToArray();
  }
}
