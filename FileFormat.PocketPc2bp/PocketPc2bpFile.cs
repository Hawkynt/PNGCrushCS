using System;
using FileFormat.Core;

namespace FileFormat.PocketPc2bp;

/// <summary>In-memory representation of a Pocket PC 2bp bitmap image.</summary>
public readonly record struct PocketPc2bpFile : IImageFormatReader<PocketPc2bpFile>, IImageToRawImage<PocketPc2bpFile>, IImageFromRawImage<PocketPc2bpFile>, IImageFormatWriter<PocketPc2bpFile> {

  internal const int HeaderSize = 8;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<PocketPc2bpFile>.PrimaryExtension => ".2bp";
  static string[] IImageFormatMetadata<PocketPc2bpFile>.FileExtensions => [".2bp"];
  static PocketPc2bpFile IImageFormatReader<PocketPc2bpFile>.FromSpan(ReadOnlySpan<byte> data) => PocketPc2bpReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PocketPc2bpFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<PocketPc2bpFile>.ToBytes(PocketPc2bpFile file) => PocketPc2bpWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PocketPc2bpFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static PocketPc2bpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
