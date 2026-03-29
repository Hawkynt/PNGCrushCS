using System;

namespace FileFormat.QuakeSpr;

/// <summary>Assembles Quake 1 sprite (.spr) file bytes from a <see cref="QuakeSprFile"/>.</summary>
public static class QuakeSprWriter {

  public static byte[] ToBytes(QuakeSprFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var totalSize = QuakeSprHeader.StructSize + QuakeSprFrameHeader.StructSize + pixelCount;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Header
    var header = new QuakeSprHeader(
      Magic: 0x50534449,
      Version: 1,
      SpriteType: file.SpriteType,
      BoundingRadius: file.BoundingRadius,
      MaxWidth: file.Width,
      MaxHeight: file.Height,
      NumFrames: file.NumFrames,
      BeamLength: file.BeamLength,
      SyncType: file.SyncType
    );
    header.WriteTo(span);

    // Frame header
    var frameHeader = new QuakeSprFrameHeader(0, 0, 0, file.Width, file.Height);
    frameHeader.WriteTo(span[QuakeSprHeader.StructSize..]);

    // Pixel data
    var pixelOffset = QuakeSprHeader.StructSize + QuakeSprFrameHeader.StructSize;
    file.PixelData.AsSpan(0, Math.Min(pixelCount, file.PixelData.Length)).CopyTo(result.AsSpan(pixelOffset));

    return result;
  }
}
