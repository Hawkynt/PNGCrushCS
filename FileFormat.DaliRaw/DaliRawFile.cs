using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DaliRaw;

/// <summary>In-memory representation of an Atari ST Dali raw image (320x200, 4 planes, 16 colors).</summary>
public sealed class DaliRawFile : IImageFileFormat<DaliRawFile> {

  /// <summary>Palette size in bytes (16 words = 32 bytes).</summary>
  public const int PaletteSize = 32;

  /// <summary>Padding size in bytes after palette.</summary>
  public const int PaddingSize = 2;

  /// <summary>Planar pixel data size.</summary>
  public const int PlanarDataSize = 32000;

  /// <summary>The exact file size: 32 + 2 + 32000 = 32034 bytes.</summary>
  public const int ExpectedFileSize = PaletteSize + PaddingSize + PlanarDataSize;

  static string IImageFileFormat<DaliRawFile>.PrimaryExtension => ".sd0";
  static string[] IImageFileFormat<DaliRawFile>.FileExtensions => [".sd0", ".sd1", ".sd2"];
  static FormatCapability IImageFileFormat<DaliRawFile>.Capabilities => FormatCapability.IndexedOnly;
  static DaliRawFile IImageFileFormat<DaliRawFile>.FromFile(FileInfo file) => DaliRawReader.FromFile(file);
  static DaliRawFile IImageFileFormat<DaliRawFile>.FromBytes(byte[] data) => DaliRawReader.FromBytes(data);
  static DaliRawFile IImageFileFormat<DaliRawFile>.FromStream(Stream stream) => DaliRawReader.FromStream(stream);
  static byte[] IImageFileFormat<DaliRawFile>.ToBytes(DaliRawFile file) => DaliRawWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 200.</summary>
  public int Height => 200;

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(DaliRawFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, 320, 200, 4);
    var paletteCount = Math.Min(16, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static DaliRawFile FromRawImage(RawImage image) => throw new NotSupportedException("DaliRaw format does not support creation from RawImage.");
}
