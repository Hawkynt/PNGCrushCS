using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PocketPc2bp;

/// <summary>In-memory representation of a Pocket PC 2bp bitmap image.</summary>
public sealed class PocketPc2bpFile : IImageFileFormat<PocketPc2bpFile> {

  internal const int HeaderSize = 8;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<PocketPc2bpFile>.PrimaryExtension => ".2bp";
  static string[] IImageFileFormat<PocketPc2bpFile>.FileExtensions => [".2bp"];
  static FormatCapability IImageFileFormat<PocketPc2bpFile>.Capabilities => FormatCapability.MonochromeOnly;
  static PocketPc2bpFile IImageFileFormat<PocketPc2bpFile>.FromFile(FileInfo file) => PocketPc2bpReader.FromFile(file);
  static PocketPc2bpFile IImageFileFormat<PocketPc2bpFile>.FromBytes(byte[] data) => PocketPc2bpReader.FromBytes(data);
  static PocketPc2bpFile IImageFileFormat<PocketPc2bpFile>.FromStream(Stream stream) => PocketPc2bpReader.FromStream(stream);
  static byte[] IImageFileFormat<PocketPc2bpFile>.ToBytes(PocketPc2bpFile file) => PocketPc2bpWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(PocketPc2bpFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
