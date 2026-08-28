using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>Paired NIfTI .hdr/.img form. Reads NIfTI-1 and NIfTI-2; writes portable NIfTI-1 pairs.</summary>
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
    var v2 = Nifti2Codec.Matches(header, pair: true);
    if (v2 == true)
      return true;
    return Nifti1Codec.Matches(header, pair: true);
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
    if (Nifti1Codec.Matches(data, pair: true) == true || Nifti2Codec.Matches(data, pair: true) == true)
      throw new InvalidDataException("NIfTI paired data requires its .img companion and must be opened by file path.");
    throw new InvalidDataException("Data is not a recognised NIfTI paired header.");
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

    var header = File.ReadAllBytes(headerPath);
    var payload = File.ReadAllBytes(imagePath);
    if (header.Length < 4)
      throw new InvalidDataException("NIfTI paired header is truncated.");

    var littleSize = BinaryPrimitives.ReadInt32LittleEndian(header);
    var bigSize = BinaryPrimitives.ReadInt32BigEndian(header);
    NiftiFile parsed;
    if (littleSize == Nifti2Codec.HeaderSize || bigSize == Nifti2Codec.HeaderSize)
      parsed = Nifti2Codec.ParsePair(header, payload);
    else if (littleSize == Nifti1Codec.HeaderSize || bigSize == Nifti1Codec.HeaderSize)
      parsed = Nifti1Codec.ParsePair(header, payload);
    else
      throw new InvalidDataException("NIfTI paired header has neither a 348-byte NIfTI-1 nor 540-byte NIfTI-2 header.");

    return new() { Nifti = parsed };
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
      throw new InvalidDataException("NIfTI-1 paired dimensions must fit signed 16-bit dim[] entries; use NIfTI-2 for larger dimensions.");

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
