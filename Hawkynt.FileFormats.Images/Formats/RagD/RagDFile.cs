using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RagD;

/// <summary>In-memory representation of a RAG-D picture (.rag) or a Music Compile 2 one (.ragc).</summary>
/// <remarks>
/// One header covering the whole range an Atari Falcon can display: one to eight bitplanes against
/// a stored palette, or sixteen bits a pixel with no palette at all. Which it is follows from the
/// plane count and the palette length together, and the two are not independent — sixteen colours
/// cannot describe an eight-plane picture, and a true-colour one has a palette it does not use.
/// <para/>
/// The chunky variant carries the same header and the same 256-colour palette but spends a whole
/// byte per pixel instead of spreading it across eight planes, which is the same picture in a
/// layout a program can draw into without shifting bits.
/// </remarks>
public readonly record struct RagDFile
  : IImageFormatReader<RagDFile>, IImageToRawImage<RagDFile>,
    IImageFromRawImage<RagDFile>, IImageFormatWriter<RagDFile> {

  /// <summary>The string every file starts with.</summary>
  public const string Signature = "RAG-D!";

  /// <summary>Offset of the palette.</summary>
  public const int PaletteOffset = 30;

  /// <summary>Size of a stored ST palette: sixteen colours of one word each.</summary>
  public const int StPaletteLength = 32;

  /// <summary>Size of a stored Falcon palette: 256 colours of four bytes each.</summary>
  public const int FalconPaletteLength = 1024;

  /// <summary>Offset of the bitmap in a file carrying a Falcon palette.</summary>
  public const int FalconBitmapOffset = PaletteOffset + FalconPaletteLength;

  static string IImageFormatMetadata<RagDFile>.PrimaryExtension => ".rag";
  static string[] IImageFormatMetadata<RagDFile>.FileExtensions => [".rag", ".ragc"];
  static RagDFile IImageFormatReader<RagDFile>.FromSpan(ReadOnlySpan<byte> data)
    => RagDReader.FromSpan(data);
  static byte[] IImageFormatWriter<RagDFile>.ToBytes(RagDFile file) => RagDWriter.ToBytes(file);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static RagDFile IImageFormatReader<RagDFile>.FromFile(FileInfo file) => RagDReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<RagDFile>.VideoModes => [
    new("RAG-D", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 65536)])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Bitplanes a pixel is spread over, or 16 for true colour.</summary>
  public int Planes { get; init; }

  /// <summary>Size of the stored palette.</summary>
  public int PaletteLength { get; init; }

  /// <summary>Whether the pixels are stored one byte each rather than as bitplanes.</summary>
  public bool IsChunky { get; init; }

  public static RawImage ToRawImage(RagDFile file) {
    var data = file.Data ?? [];
    int width = file.Width, height = file.Height;

    if (file.Planes == 16) {
      var rgb = new byte[width * height * 3];
      for (var i = 0; i < width * height; ++i)
        AtariStGraphics.FalconTrueColorToRgb(data, FalconBitmapOffset + i * 2, rgb.AsSpan(i * 3, 3));

      return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
    }

    var colors = file.PaletteLength == StPaletteLength ? 16 : 256;
    var palette = file.PaletteLength == StPaletteLength
      ? AtariStGraphics.ReadPalette(data, PaletteOffset, colors)
      : AtariStGraphics.ReadFalconPalette(data, PaletteOffset, colors);

    var bitmapOffset = PaletteOffset + file.PaletteLength;
    var pixels = new byte[width * height];

    if (file.IsChunky)
      data.AsSpan(bitmapOffset, Math.Min(pixels.Length, data.Length - bitmapOffset)).CopyTo(pixels);
    else
      pixels = AtariStGraphics.UnpackBitplanes(
        data, bitmapOffset, (width >> 3) * file.Planes, file.Planes, width, height);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = colors,
    };
  }

  /// <summary>Bitplanes a written picture spreads a pixel over.</summary>
  public const int WrittenPlanes = 8;

  /// <summary>Colours a written picture's palette holds.</summary>
  public const int WrittenColorCount = 256;

  /// <summary>Pixels a row must be a whole number of; the hardware fetched a word at a time.</summary>
  public const int WidthGranularity = 16;

  /// <summary>The widest row the header can state, being a whole number of words.</summary>
  public const int MaxWidth = 65535 / WidthGranularity * WidthGranularity;

  /// <summary>The tallest picture the header can state, its height being stored one less.</summary>
  public const int MaxHeight = 65536;

  /// <summary>
  /// Builds a picture from any image, as eight bitplanes against a 256-colour Falcon palette.
  /// </summary>
  /// <remarks>
  /// Of the forms the header can state, this is the one that loses least. True colour is sixteen
  /// bits a pixel in five-six-five, so it throws away three bits of red and blue from every pixel
  /// and can never be exact; eight planes against a stored palette keeps whatever 256 colours the
  /// picture is reduced to exactly as they were. Bitplanes rather than the chunky variant because
  /// the two are the same length and nothing in the header tells them apart — only the file name
  /// does, and a picture read back without one is taken as bitplanes.
  /// <para/>
  /// A row is a whole number of words, so a width that is not a multiple of sixteen is sampled up to
  /// the next one rather than refused.
  /// </remarks>
  public static RagDFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = (image.Width + WidthGranularity - 1) / WidthGranularity * WidthGranularity;
    if (width > MaxWidth || image.Height > MaxHeight)
      throw new ArgumentException(
        $"A RAG-D header states {MaxWidth}x{MaxHeight} at most, not {width}x{image.Height}.", nameof(image));

    var height = image.Height;
    var indexed = image.SampleTo(width, height).EnsureIndexedAtMost(WrittenColorCount);
    var stride = (width >> 3) * WrittenPlanes;
    var data = new byte[FalconBitmapOffset + stride * height];

    for (var i = 0; i < Signature.Length; ++i)
      data[i] = (byte)Signature[i];

    data[12] = (byte)(width >> 8);
    data[13] = (byte)width;

    // The stored height is one less than the real one, so a 256-row picture still fits two bytes.
    data[14] = (byte)((height - 1) >> 8);
    data[15] = (byte)(height - 1);
    data[17] = WrittenPlanes;
    data[18] = (byte)(FalconPaletteLength >> 24);
    data[19] = (byte)(FalconPaletteLength >> 16);
    data[20] = (byte)(FalconPaletteLength >> 8);
    data[21] = (byte)(FalconPaletteLength & 0xFF);

    AtariStGraphics.WriteFalconPalette(
      indexed.Palette ?? [], WrittenColorCount, data.AsSpan(PaletteOffset, FalconPaletteLength));
    AtariStGraphics.PackBitplanes(indexed.PixelData, stride, WrittenPlanes, width, height)
      .CopyTo(data.AsSpan(FalconBitmapOffset));

    return new() {
      Data = data,
      Width = width,
      Height = height,
      Planes = WrittenPlanes,
      PaletteLength = FalconPaletteLength,
      IsChunky = false,
    };
  }
}
