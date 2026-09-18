using System;

namespace FileFormat.Zoomatic;

/// <summary>Assembles Zoomatic (.zom) file bytes from a ZoomaticFile.</summary>
public static class ZoomaticWriter {

  public static byte[] ToBytes(ZoomaticFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var unpacked = new byte[ZoomaticFile.UnpackedSize];
    (file.BitmapData ?? []).AsSpan(0, ZoomaticFile.BitmapDataSize).CopyTo(unpacked.AsSpan(ZoomaticFile.BitmapOffset));
    (file.ScreenData ?? []).AsSpan(0, ZoomaticFile.ScreenDataSize).CopyTo(unpacked.AsSpan(ZoomaticFile.ScreenOffset));
    (file.ColorData ?? []).AsSpan(0, ZoomaticFile.ColorDataSize).CopyTo(unpacked.AsSpan(ZoomaticFile.ColorOffset));
    unpacked[ZoomaticFile.BackgroundOffset] = file.BackgroundColor;

    return ZoomaticCompression.Pack(unpacked, file.LoadAddress);
  }
}
