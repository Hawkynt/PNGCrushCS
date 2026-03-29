using System;
using System.IO;

namespace FileFormat.Wal;

/// <summary>Assembles WAL file bytes from a WalFile model.</summary>
public static class WalWriter {

  public static byte[] ToBytes(WalFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;

    var mip0Size = width * height;
    var mip1Width = width / 2;
    var mip1Height = height / 2;
    var mip1Size = mip1Width * mip1Height;
    var mip2Width = mip1Width / 2;
    var mip2Height = mip1Height / 2;
    var mip2Size = mip2Width * mip2Height;
    var mip3Width = mip2Width / 2;
    var mip3Height = mip2Height / 2;
    var mip3Size = mip3Width * mip3Height;

    var mip0Offset = (uint)WalHeader.StructSize;
    var mip1Offset = mip0Offset + (uint)mip0Size;
    var mip2Offset = mip1Offset + (uint)mip1Size;
    var mip3Offset = mip2Offset + (uint)mip2Size;

    var hasMipMaps = file.MipMaps is { Length: 3 };
    var totalSize = (int)mip0Offset + mip0Size;
    if (hasMipMaps)
      totalSize = (int)mip3Offset + mip3Size;

    var name = file.Name;
    if (name is { Length: > 32 })
      name = name[..32];

    var nextFrameName = file.NextFrameName;
    if (nextFrameName is { Length: > 32 })
      nextFrameName = nextFrameName[..32];

    var header = new WalHeader(
      name,
      (uint)width,
      (uint)height,
      mip0Offset,
      hasMipMaps ? mip1Offset : 0,
      hasMipMaps ? mip2Offset : 0,
      hasMipMaps ? mip3Offset : 0,
      nextFrameName,
      file.Flags,
      file.Contents,
      file.Value
    );

    var result = new byte[totalSize];
    header.WriteTo(result);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, mip0Size)).CopyTo(result.AsSpan((int)mip0Offset));

    if (hasMipMaps) {
      file.MipMaps![0].AsSpan(0, Math.Min(file.MipMaps[0].Length, mip1Size)).CopyTo(result.AsSpan((int)mip1Offset));
      file.MipMaps[1].AsSpan(0, Math.Min(file.MipMaps[1].Length, mip2Size)).CopyTo(result.AsSpan((int)mip2Offset));
      file.MipMaps[2].AsSpan(0, Math.Min(file.MipMaps[2].Length, mip3Size)).CopyTo(result.AsSpan((int)mip3Offset));
    }

    return result;
  }
}
