using System;
using FileFormat.Core;

namespace FileFormat.Spectrum512Smoosh;

/// <summary>In-memory representation of an Atari ST Spectrum 512 Smooshed (SPS) image (320x199, 512 colors).</summary>
public readonly record struct Spectrum512SmooshFile : IImageFormatReader<Spectrum512SmooshFile>, IImageToRawImage<Spectrum512SmooshFile>, IImageFormatWriter<Spectrum512SmooshFile> {

  /// <summary>Minimum file size for validation.</summary>
  public const int MinFileSize = 4;

  static string IImageFormatMetadata<Spectrum512SmooshFile>.PrimaryExtension => ".sps";
  static string[] IImageFormatMetadata<Spectrum512SmooshFile>.FileExtensions => [".sps"];
  static Spectrum512SmooshFile IImageFormatReader<Spectrum512SmooshFile>.FromSpan(ReadOnlySpan<byte> data) => Spectrum512SmooshReader.FromSpan(data);
  static byte[] IImageFormatWriter<Spectrum512SmooshFile>.ToBytes(Spectrum512SmooshFile file) => Spectrum512SmooshWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 199.</summary>
  public int Height => 199;

  /// <summary>The raw smooshed data bytes.</summary>
  public byte[] RawData { get; init; }

  /// <summary>
  /// Refuses the picture, the smooshed packing not being decoded here.
  /// </summary>
  /// <remarks>
  /// What this used to return was a black picture of the right size, and nothing marked it as
  /// anything else — so a file that had not been decoded at all counted as a decode, and converting
  /// one would have written the black out as though it were the picture.
  /// <para/>
  /// The packing keeps a palette per scanline and codes the two apart from one another; none of that
  /// is implemented, and saying so is the only honest answer until it is.
  /// </remarks>
  public static RawImage ToRawImage(Spectrum512SmooshFile file)
    => throw new NotSupportedException("A smooshed Spectrum 512 picture is not decoded here; only the file itself is recognised.");

}
