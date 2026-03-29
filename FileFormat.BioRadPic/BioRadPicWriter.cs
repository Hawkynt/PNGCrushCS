using System;

namespace FileFormat.BioRadPic;

/// <summary>Assembles Bio-Rad PIC file bytes from an in-memory representation.</summary>
public static class BioRadPicWriter {

  public static byte[] ToBytes(BioRadPicFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerPixel = file.ByteFormat ? 1 : 2;
    var pixelDataSize = file.Width * file.Height * bytesPerPixel;
    var fileSize = BioRadPicHeader.StructSize + pixelDataSize;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new BioRadPicHeader(
      Nx: (ushort)file.Width,
      Ny: (ushort)file.Height,
      Npic: (ushort)file.NumImages,
      Ramp1Min: file.Ramp1Min,
      Ramp1Max: file.Ramp1Max,
      Notes: file.Notes,
      ByteFormat: file.ByteFormat ? (short)1 : (short)0,
      ImageNumber: 0,
      Name: file.Name ?? "",
      Merged: file.Merged,
      Color1: file.Color1,
      FileId: BioRadPicHeader.MagicFileId,
      Ramp2Min: file.Ramp2Min,
      Ramp2Max: file.Ramp2Max,
      Color2: file.Color2,
      Edited: file.Edited,
      Lens: (short)file.Lens,
      MagFactor: file.MagFactor
    );

    header.WriteTo(span);

    var copyLen = Math.Min(pixelDataSize, file.PixelData.Length);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(BioRadPicHeader.StructSize));

    return result;
  }
}
