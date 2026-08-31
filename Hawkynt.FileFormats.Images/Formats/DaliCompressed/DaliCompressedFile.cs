using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.DaliCompressed;

/// <summary>In-memory representation of a compressed Atari ST Dali screen.</summary>
/// <remarks>
/// A 32-byte palette, then two lengths written as ASCII decimal each followed by CR LF, then the
/// run-count stream and the four-byte-value stream back to back. The file extension is the only
/// resolution indicator: .LPK, .MPK, and .HPK select low, medium, and high resolution respectively.
/// </remarks>
public readonly record struct DaliCompressedFile
  : IImageFormatReader<DaliCompressedFile>, IImageToRawImage<DaliCompressedFile>,
    IImageFromRawImage<DaliCompressedFile>, IImageFormatWriter<DaliCompressedFile> {

  /// <summary>Size of the stored Atari ST palette block.</summary>
  public const int PaletteSize = 32;

  /// <summary>Offset of the first ASCII length field.</summary>
  public const int LengthsOffset = PaletteSize;

  static string IImageFormatMetadata<DaliCompressedFile>.PrimaryExtension => ".lpk";
  static string[] IImageFormatMetadata<DaliCompressedFile>.FileExtensions => [".lpk", ".mpk", ".hpk"];
  static DaliCompressedFile IImageFormatReader<DaliCompressedFile>.FromSpan(ReadOnlySpan<byte> data)
    => DaliCompressedReader.FromSpan(data);
  static DaliCompressedFile IImageFormatReader<DaliCompressedFile>.FromFile(FileInfo file)
    => DaliCompressedReader.FromFile(file);
  static DaliCompressedFile IImageFromRawImage<DaliCompressedFile>.FromRawImage(RawImage image, string extension)
    => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<DaliCompressedFile>.ToBytes(DaliCompressedFile file)
    => DaliCompressedWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<DaliCompressedFile>.VideoModes => [
    new("Low resolution", [(320, 200)], [16]),
    new("Medium resolution", [(640, 200)], [4]),
    new("High resolution", [(640, 400)], [2]),
  ];

  /// <summary>Which ST resolution the screen holds.</summary>
  public DaliResolution Resolution { get; init; }

  /// <summary>All sixteen Atari ST palette words as the exact 32 stored big-endian bytes.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Exactly 32,000 uncompressed Atari ST screen bytes.</summary>
  public byte[] ScreenData { get; init; }

  public static RawImage ToRawImage(DaliCompressedFile file) {
    Validate(file, nameof(file));
    var (width, height, planes) = Geometry(file.Resolution);
    var colors = 1 << planes;

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = PlanarConverter.AtariStToChunky(file.ScreenData, width, height, planes),
      Palette = planes == 1
        ? AtariStGraphics.MonochromePalette()
        : AtariStGraphics.ReadPalette(file.Palette, 0, colors, false),
      PaletteCount = colors,
    };
  }

  /// <summary>Encodes the primary .LPK low-resolution variant.</summary>
  public static DaliCompressedFile FromRawImage(RawImage image) => FromRawImage(image, DaliResolution.Low);

  /// <summary>Encodes at the resolution selected by .LPK, .MPK, or .HPK.</summary>
  public static DaliCompressedFile FromRawImage(RawImage image, string extension)
    => FromRawImage(image, ResolutionFromExtension(extension));

  /// <summary>Encodes a specific Dali compressed resolution without resizing or clipping.</summary>
  public static DaliCompressedFile FromRawImage(RawImage image, DaliResolution resolution) {
    ArgumentNullException.ThrowIfNull(image);
    var (width, height, planes) = Geometry(resolution);
    if (image.Width != width || image.Height != height)
      throw new ArgumentException($"{resolution} compressed Dali images must be exactly {width}x{height} pixels.", nameof(image));
    if (!image.HasEnoughPixelData)
      throw new ArgumentException("The source image does not contain enough pixel data for its dimensions.", nameof(image));

    var palette = new byte[PaletteSize];
    byte[] indices;

    if (resolution == DaliResolution.High) {
      var rgb = image.EnsureAnyFormat(PixelFormat.Rgb24);
      indices = new byte[width * height];
      for (var i = 0; i < indices.Length; ++i) {
        var at = i * 3;
        var luma = (299 * rgb.PixelData[at] + 587 * rgb.PixelData[at + 1] + 114 * rgb.PixelData[at + 2] + 500) / 1000;
        indices[i] = luma < 128 ? (byte)1 : (byte)0;
      }
    } else {
      var colors = 1 << planes;
      var indexed = image.EnsureIndexedAtMost(colors);
      if (indexed.Palette is null || indexed.PaletteCount is < 1 || indexed.PaletteCount > colors
          || indexed.Palette.Length < indexed.PaletteCount * 3)
        throw new ArgumentException($"{resolution} compressed Dali images require between 1 and {colors} valid palette entries.", nameof(image));
      if (indexed.PixelData.Length != width * height)
        throw new ArgumentException("Indexed compressed Dali input must contain exactly one palette index per pixel.", nameof(image));

      foreach (var index in indexed.PixelData)
        if (index >= indexed.PaletteCount)
          throw new ArgumentException("A compressed Dali pixel index exceeds the selected palette.", nameof(image));

      indices = indexed.PixelData;
      var stored = PlanarConverter.RgbToStPalette(indexed.Palette, indexed.PaletteCount);
      for (var i = 0; i < Math.Min(stored.Length, 16); ++i)
        BinaryPrimitives.WriteInt16BigEndian(palette.AsSpan(i * 2), stored[i]);
    }

    return new() {
      Resolution = resolution,
      Palette = palette,
      ScreenData = PlanarConverter.ChunkyToAtariSt(indices, width, height, planes),
    };
  }

  /// <summary>Formats a length the way the header stores it: ASCII decimal, then CR LF.</summary>
  internal static byte[] FormatLength(int value) {
    if (value <= 0)
      throw new ArgumentOutOfRangeException(nameof(value), value, "Compressed Dali stream lengths must be positive.");

    return Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture) + "\r\n");
  }

  internal static DaliResolution ResolutionFromExtension(string extension) {
    ArgumentException.ThrowIfNullOrWhiteSpace(extension);
    return extension.ToLowerInvariant() switch {
      ".lpk" => DaliResolution.Low,
      ".mpk" => DaliResolution.Medium,
      ".hpk" => DaliResolution.High,
      _ => throw new ArgumentException("Compressed Dali extension must be .lpk, .mpk, or .hpk.", nameof(extension)),
    };
  }

  internal static (int Width, int Height, int Planes) Geometry(DaliResolution resolution) => resolution switch {
    DaliResolution.Low => (320, 200, 4),
    DaliResolution.Medium => (640, 200, 2),
    DaliResolution.High => (640, 400, 1),
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown compressed Dali resolution."),
  };

  internal static void Validate(DaliCompressedFile file, string parameterName) {
    _ = Geometry(file.Resolution);
    if (file.Palette is null || file.Palette.Length != PaletteSize)
      throw new ArgumentException($"Compressed Dali palette must contain exactly {PaletteSize} bytes.", parameterName);
    if (file.ScreenData is null || file.ScreenData.Length != DaliCompressor.ScreenSize)
      throw new ArgumentException($"Compressed Dali screen must contain exactly {DaliCompressor.ScreenSize} bytes.", parameterName);
  }
}
