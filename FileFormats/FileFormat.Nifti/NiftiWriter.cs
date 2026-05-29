using System;

namespace FileFormat.Nifti;

/// <summary>Assembles NIfTI neuroimaging file bytes from voxel data.</summary>
public static class NiftiWriter {

  public static byte[] ToBytes(NiftiFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(NiftiFile file) {
    var voxOffset = file.VoxOffset;
    if (voxOffset < NiftiHeader.StructSize)
      voxOffset = 352f;

    var pixelDataStart = (int)voxOffset;
    var fileSize = pixelDataStart + file.PixelData.Length;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var ndims = (short)(file.Depth > 1 ? 3 : 2);

    var dim = new short[8];
    dim[0] = ndims;
    dim[1] = (short)file.Width;
    dim[2] = (short)file.Height;
    if (ndims >= 3)
      dim[3] = (short)file.Depth;

    var header = new NiftiHeader {
      SizeOfHdr = NiftiHeader.StructSize,
      Dim = dim,
      Datatype = (short)file.Datatype,
      Bitpix = file.Bitpix,
      Pixdim = file.Pixdim.Length > 0 ? file.Pixdim : new float[8],
      VoxOffset = voxOffset,
      SclSlope = file.SclSlope,
      SclInter = file.SclInter,
      Descrip = file.Description,
      Magic = "n+1\0"
    };

    header.WriteTo(span);

    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(pixelDataStart));

    return result;
  }
}
