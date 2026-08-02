using System;

namespace FileFormat.AtariPaintworks;

/// <summary>Assembles Atari ST Paintworks/GFA/DeskPic file bytes from an AtariPaintworksFile.</summary>
public static class AtariPaintworksWriter {

  public static byte[] ToBytes(AtariPaintworksFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariPaintworksFile.FileSize];
    var span = result.AsSpan();

    // The picture's resolution goes in the long word the file opens with. It was never written, so
    // every file said nought — the lowest resolution — and a 640 by 400 picture came back 320 by 200
    // drawn from the wrong part of its own data. The reader takes it from here.
    span[3] = (byte)file.Resolution;

    new AtariPaintworksHeader(file.Palette).WriteTo(span[AtariPaintworksFile.PaletteOffset..]);
    AtariPaintworksFile.Signature.CopyTo(span[AtariPaintworksFile.SignatureOffset..]);
    // The flags byte carries the resolution as well as the long word does, and other readers go by
    // it: the samples have bit five clear at low resolution and set at high, so writing the low
    // flags for everything left a monochrome picture being read as a sixteen-colour one.
    span[AtariPaintworksFile.FlagsOffset] = (byte)(AtariPaintworksFile.LowResolutionFlags
      | (file.Resolution == AtariPaintworksResolution.High ? AtariPaintworksFile.HighResolutionFlag : 0));

    file.PixelData.AsSpan(0, Math.Min(AtariPaintworksFile.BitmapDataSize, file.PixelData.Length))
      .CopyTo(span[AtariPaintworksFile.BitmapOffset..]);

    return result;
  }
}
