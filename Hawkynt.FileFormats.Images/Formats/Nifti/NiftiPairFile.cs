using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>NIfTI-1 paired-file form: a 348-byte .hdr header with voxel bytes in a sibling .img.</summary>
[FormatDetectionPriority(90)]
public sealed class NiftiPairFile :
  IImageFormatReader<NiftiPairFile>, IImageToRawImage<NiftiPairFile>,
  IImageFromRawImage<NiftiPairFile>, IImageFormatWriter<NiftiPairFile> {

  static string IImageFormatMetadata<NiftiPairFile>.PrimaryExtension => ".hdr";
  static string[] IImageFormatMetadata<NiftiPairFile>.FileExtensions => [".hdr", ".img"];
  static NiftiPairFile IImageFormatReader<NiftiPairFile>.FromSpan(ReadOnlySpan<byte> data) => NiftiPairReader.FromSpan(data);
  static NiftiPairFile IImageFormatReader<NiftiPairFile>.FromFile(FileInfo file) => NiftiPairReader.FromFile(file);
  static byte[] IImageFormatWriter<NiftiPairFile>.ToBytes(NiftiPairFile file) => NiftiPairWriter.ToBytes(file);
  static void IImageFormatWriter<NiftiPairFile>.WriteCompanions(NiftiPairFile file, FileInfo target) => NiftiPairWriter.WriteCompanions(file, target);

  public NiftiFile Nifti { get; init; } = new();

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < NiftiHeader.StructSize)
      return null;
    var magic = header.Slice(344, 4);
    return magic.SequenceEqual("ni1\0"u8) ? true : null;
  }

  public static RawImage ToRawImage(NiftiPairFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return NiftiFile.ToRawImage(file.Nifti);
  }

  public static NiftiPairFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Nifti = NiftiPairWriter.AsPair(NiftiFile.FromRawImage(image)) };
  }
}

public static class NiftiPairReader {

  public static NiftiPairFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < NiftiHeader.StructSize)
      throw new InvalidDataException("Data is too small for a NIfTI-1 paired header.");
    var header = NiftiHeader.ReadFrom(data);
    if (header.SizeOfHdr != NiftiHeader.StructSize || header.Magic != "ni1")
      throw new InvalidDataException("NIfTI paired data requires its .img companion and must be opened by file path.");
    throw new InvalidDataException("NIfTI paired data requires its .img companion and must be opened by file path.");
  }

  public static NiftiPairFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);

    var extension = file.Extension;
    var headerPath = string.Equals(extension, ".img", StringComparison.OrdinalIgnoreCase)
      ? Path.ChangeExtension(file.FullName, ".hdr")
      : file.FullName;
    var imagePath = string.Equals(extension, ".img", StringComparison.OrdinalIgnoreCase)
      ? file.FullName
      : Path.ChangeExtension(file.FullName, ".img");

    if (!File.Exists(headerPath))
      throw new FileNotFoundException("NIfTI paired header not found.", headerPath);
    if (!File.Exists(imagePath))
      throw new FileNotFoundException("NIfTI paired voxel companion not found.", imagePath);

    var bytes = File.ReadAllBytes(headerPath);
    if (bytes.Length < NiftiHeader.StructSize)
      throw new InvalidDataException("NIfTI paired header is truncated.");

    var header = NiftiHeader.ReadFrom(bytes);
    if (header.SizeOfHdr != NiftiHeader.StructSize)
      throw new InvalidDataException($"Invalid NIfTI SizeOfHdr: expected {NiftiHeader.StructSize}, got {header.SizeOfHdr}.");
    if (header.Magic != "ni1")
      throw new InvalidDataException($"NIfTI paired header has magic '{header.Magic}', expected 'ni1'.");

    var ndims = header.Dim[0];
    var width = ndims >= 1 ? header.Dim[1] : 1;
    var height = ndims >= 2 ? header.Dim[2] : 1;
    var depth = ndims >= 3 ? header.Dim[3] : 1;
    if (width < 1 || height < 1 || depth < 1)
      throw new InvalidDataException("NIfTI paired header contains a non-positive image dimension.");

    var payload = File.ReadAllBytes(imagePath);
    var start = Math.Max(0, (int)header.VoxOffset);
    if (start > payload.Length)
      throw new InvalidDataException("NIfTI vox_offset exceeds the .img companion length.");
    var pixels = payload[start..];

    return new() {
      Nifti = new NiftiFile {
        Width = width,
        Height = height,
        Depth = depth,
        Datatype = (NiftiDataType)header.Datatype,
        Bitpix = header.Bitpix,
        SclSlope = header.SclSlope,
        SclInter = header.SclInter,
        VoxOffset = 0,
        Description = header.Descrip,
        PixelData = pixels,
        Pixdim = header.Pixdim,
      },
    };
  }
}

public static class NiftiPairWriter {

  public static NiftiFile AsPair(NiftiFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Depth = file.Depth,
    Datatype = file.Datatype,
    Bitpix = file.Bitpix,
    SclSlope = file.SclSlope,
    SclInter = file.SclInter,
    VoxOffset = 0,
    Description = file.Description,
    PixelData = file.PixelData,
    Pixdim = file.Pixdim,
  };

  public static byte[] ToBytes(NiftiPairFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var model = file.Nifti;
    if (model.Width is < 1 or > short.MaxValue || model.Height is < 1 or > short.MaxValue || model.Depth is < 1 or > short.MaxValue)
      throw new InvalidDataException("NIfTI-1 paired dimensions must fit signed 16-bit dim[] entries.");

    var dim = new short[8];
    var ndims = (short)(model.Depth > 1 ? 3 : 2);
    dim[0] = ndims;
    dim[1] = (short)model.Width;
    dim[2] = (short)model.Height;
    if (ndims >= 3)
      dim[3] = (short)model.Depth;

    var header = new NiftiHeader {
      SizeOfHdr = NiftiHeader.StructSize,
      Dim = dim,
      Datatype = (short)model.Datatype,
      Bitpix = model.Bitpix,
      Pixdim = model.Pixdim.Length > 0 ? model.Pixdim : new float[8],
      VoxOffset = 0,
      SclSlope = model.SclSlope,
      SclInter = model.SclInter,
      Descrip = model.Description,
      Magic = "ni1\0",
    };

    var result = new byte[NiftiHeader.StructSize];
    header.WriteTo(result);
    return result;
  }

  public static void WriteCompanions(NiftiPairFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(file);
    ArgumentNullException.ThrowIfNull(target);
    var imagePath = string.Equals(target.Extension, ".img", StringComparison.OrdinalIgnoreCase)
      ? target.FullName
      : Path.ChangeExtension(target.FullName, ".img");
    File.WriteAllBytes(imagePath, file.Nifti.PixelData ?? []);
  }
}
