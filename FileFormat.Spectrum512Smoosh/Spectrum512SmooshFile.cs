using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Spectrum512Smoosh;

/// <summary>In-memory representation of an Atari ST Spectrum 512 Smooshed (SPS) image (320x199, 512 colors). Stub implementation.</summary>
public sealed class Spectrum512SmooshFile : IImageFileFormat<Spectrum512SmooshFile> {

  /// <summary>Minimum file size for validation.</summary>
  public const int MinFileSize = 4;

  static string IImageFileFormat<Spectrum512SmooshFile>.PrimaryExtension => ".sps";
  static string[] IImageFileFormat<Spectrum512SmooshFile>.FileExtensions => [".sps"];
  static Spectrum512SmooshFile IImageFileFormat<Spectrum512SmooshFile>.FromFile(FileInfo file) => Spectrum512SmooshReader.FromFile(file);
  static Spectrum512SmooshFile IImageFileFormat<Spectrum512SmooshFile>.FromBytes(byte[] data) => Spectrum512SmooshReader.FromBytes(data);
  static Spectrum512SmooshFile IImageFileFormat<Spectrum512SmooshFile>.FromStream(Stream stream) => Spectrum512SmooshReader.FromStream(stream);
  static byte[] IImageFileFormat<Spectrum512SmooshFile>.ToBytes(Spectrum512SmooshFile file) => Spectrum512SmooshWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 199.</summary>
  public int Height => 199;

  /// <summary>The raw smooshed data bytes.</summary>
  public byte[] RawData { get; init; } = [];

  public static RawImage ToRawImage(Spectrum512SmooshFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Stub: the smoosh decompression format is complex; return a black image
    const int width = 320;
    const int height = 199;
    var rgb = new byte[width * height * 3];

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static Spectrum512SmooshFile FromRawImage(RawImage image) => throw new NotSupportedException("Spectrum512Smoosh format does not support creation from RawImage.");
}
