using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Vtf;

namespace FileFormat.Vtf.Tests;

/// <summary>Builds minimal VTF byte arrays for testing.</summary>
internal static class VtfTestHelper {

  public static byte[] BuildMinimalVtf(int width, int height, VtfFormat format, int mipmapCount, int frames = 1, byte[]? thumbnailData = null) {
    using var ms = new MemoryStream();

    var lowResFormat = thumbnailData != null ? VtfFormat.Dxt1 : VtfFormat.None;
    byte lowResWidth = 0;
    byte lowResHeight = 0;

    if (thumbnailData != null) {
      lowResWidth = 4;
      lowResHeight = 4;
    }

    var header = new VtfHeader(
      Sig0: (byte)'V',
      Sig1: (byte)'T',
      Sig2: (byte)'F',
      Sig3: 0,
      VersionMajor: 7,
      VersionMinor: 2,
      HeaderSize: VtfHeader.StructSize,
      Width: (short)width,
      Height: (short)height,
      Flags: 0,
      Frames: (short)frames,
      FirstFrame: 0,
      Padding0: 0,
      ReflectivityR: 0f,
      ReflectivityG: 0f,
      ReflectivityB: 0f,
      Padding1: 0,
      BumpmapScale: 1.0f,
      HighResFormat: (int)format,
      MipmapCount: (byte)mipmapCount,
      LowResFormat: (int)lowResFormat,
      LowResWidth: lowResWidth,
      LowResHeight: lowResHeight,
      Padding2: 0
    );

    var headerBytes = new byte[VtfHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Write thumbnail
    if (thumbnailData != null)
      ms.Write(thumbnailData);

    // Write mipmap surfaces smallest-to-largest
    for (var mip = mipmapCount - 1; mip >= 0; --mip) {
      var mipWidth = Math.Max(1, width >> mip);
      var mipHeight = Math.Max(1, height >> mip);
      var dataSize = VtfReader._CalculateDataSize(mipWidth, mipHeight, format);

      for (var frame = 0; frame < frames; ++frame) {
        var surfaceData = new byte[dataSize];
        for (var i = 0; i < surfaceData.Length; ++i)
          surfaceData[i] = (byte)((mip * 37 + frame * 13 + i) % 256);

        ms.Write(surfaceData);
      }
    }

    return ms.ToArray();
  }
}
