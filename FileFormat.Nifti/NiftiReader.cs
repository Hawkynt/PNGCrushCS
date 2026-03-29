using System;
using System.IO;

namespace FileFormat.Nifti;

/// <summary>Reads NIfTI neuroimaging files from bytes, streams, or file paths.</summary>
public static class NiftiReader {

  public static NiftiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NIfTI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NiftiFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static NiftiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NiftiHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid NIfTI file.");

    var header = NiftiHeader.ReadFrom(data.AsSpan());

    if (header.SizeOfHdr != NiftiHeader.StructSize)
      throw new InvalidDataException($"Invalid NIfTI SizeOfHdr: expected {NiftiHeader.StructSize}, got {header.SizeOfHdr}.");

    var magic = header.Magic;
    if (magic != "ni1" && magic != "n+1")
      throw new InvalidDataException($"Invalid NIfTI magic: \"{magic}\".");

    var ndims = header.Dim[0];
    var width = ndims >= 1 ? header.Dim[1] : 1;
    var height = ndims >= 2 ? header.Dim[2] : 1;
    var depth = ndims >= 3 ? header.Dim[3] : 1;

    var voxOffset = header.VoxOffset;
    var pixelDataStart = (int)voxOffset;
    var pixelData = Array.Empty<byte>();

    if (pixelDataStart < data.Length) {
      var available = data.Length - pixelDataStart;
      pixelData = new byte[available];
      data.AsSpan(pixelDataStart, available).CopyTo(pixelData.AsSpan(0));
    }

    return new NiftiFile {
      Width = width,
      Height = height,
      Depth = depth,
      Datatype = (NiftiDataType)header.Datatype,
      Bitpix = header.Bitpix,
      SclSlope = header.SclSlope,
      SclInter = header.SclInter,
      VoxOffset = voxOffset,
      Description = header.Descrip,
      PixelData = pixelData,
      Pixdim = header.Pixdim
    };
  }
}
