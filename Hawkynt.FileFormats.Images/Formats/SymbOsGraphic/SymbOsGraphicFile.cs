using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.SymbOsGraphic;

/// <summary>In-memory representation of a SymbOS graphic (.sgx).</summary>
/// <remarks>
/// A picture assembled from tiles rather than stored as one bitmap. SymbOS ran on eight-bit
/// machines whose memory came in banks, so a graphic larger than a bank could not be one object —
/// it is a row of chunks, a marker, another row, and so on, each chunk small enough to load and
/// draw on its own.
/// <para/>
/// Chunks come in two kinds and a picture may mix them: a short header for four colours and a
/// longer one for sixteen. The four-colour form packs its pixels the way the Amstrad's hardware
/// does, with a byte holding the low bits of four pixels in one nibble and their high bits in the
/// other, rather than two bits of each pixel side by side.
/// </remarks>
public readonly record struct SymbOsGraphicFile
  : IImageFormatReader<SymbOsGraphicFile>, IImageToRawImage<SymbOsGraphicFile>,
    IImageFromRawImage<SymbOsGraphicFile>, IImageFormatWriter<SymbOsGraphicFile> {

  /// <summary>The chunk header that ends a row of chunks.</summary>
  public const byte RowMarker = 255;

  /// <summary>The chunk header that introduces the sixteen-colour form.</summary>
  public const byte WideHeader = 64;

  /// <summary>Widest a four-colour chunk's stride may be before the wide header is needed.</summary>
  public const int MaxNarrowStride = 63;

  /// <summary>The four colours a narrow chunk draws from.</summary>
  public static ReadOnlySpan<byte> FourColorPalette => [
    0xFF, 0xFF, 0xFF, 0xAA, 0xAA, 0xAA, 0x00, 0x00, 0x00, 0x55, 0x55, 0x55,
  ];

  /// <summary>The sixteen colours a wide chunk draws from.</summary>
  public static ReadOnlySpan<byte> SixteenColorPalette => [
    0xFF, 0xFF, 0x80, 0x00, 0x00, 0x00, 0xFF, 0x80, 0x00, 0x80, 0x00, 0x00,
    0x00, 0xFF, 0xFF, 0x00, 0x00, 0x80, 0x80, 0x80, 0xFF, 0x00, 0x00, 0xFF,
    0xFF, 0xFF, 0xFF, 0x00, 0x80, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
    0xFF, 0xFF, 0x00, 0x80, 0x80, 0x80, 0xFF, 0x80, 0x80, 0xFF, 0x00, 0x00,
  ];

  static string IImageFormatMetadata<SymbOsGraphicFile>.PrimaryExtension => ".sgx";
  static string[] IImageFormatMetadata<SymbOsGraphicFile>.FileExtensions => [".sgx"];
  static SymbOsGraphicFile IImageFormatReader<SymbOsGraphicFile>.FromSpan(ReadOnlySpan<byte> data)
    => SymbOsGraphicReader.FromSpan(data);
  static byte[] IImageFormatWriter<SymbOsGraphicFile>.ToBytes(SymbOsGraphicFile file)
    => SymbOsGraphicWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SymbOsGraphicFile>.VideoModes => [
    new("SymbOS", [(IntegerRange.Any, IntegerRange.Any)], [16])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The chunks in the order they are drawn.</summary>
  public IReadOnlyList<SymbOsChunk> Chunks { get; init; }

  public static RawImage ToRawImage(SymbOsGraphicFile file) {
    var data = file.Data ?? [];
    var rgb = new byte[file.Width * file.Height * 3];

    foreach (var chunk in file.Chunks ?? []) {
      var palette = chunk.IsWide ? SixteenColorPalette : FourColorPalette;

      for (var y = 0; y < chunk.Height; ++y)
      for (var x = 0; x < chunk.Width; ++x) {
        var row = chunk.DataOffset + y * chunk.Stride;
        int index;

        if (chunk.IsWide)
          index = MsxGraphics.GetNibble(data, row, x);
        else {
          var at = row + (x >> 2);
          // One nibble holds four pixels' low bits and the other their high bits.
          var b = at < data.Length ? data[at] >> (~x & 3) : 0;
          index = ((b >> 3) & 2) | (b & 1);
        }

        var entry = index * 3;
        var target = ((chunk.Top + y) * file.Width + chunk.Left + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Bytes a sixteen-colour chunk's header occupies.</summary>
  public const int WideHeaderSize = 8;

  /// <summary>The second byte of a sixteen-colour chunk's header, which the reader insists on.</summary>
  public const byte WideHeaderKind = 5;

  /// <summary>The largest a chunk's width or height may be, both being stored as words.</summary>
  public const int MaxChunkExtent = 65535;

  /// <summary>Builds a graphic from any image, as one sixteen-colour chunk covering the whole of it.</summary>
  /// <remarks>
  /// A picture is written as a single chunk rather than a grid of them. The chunking exists because
  /// SymbOS ran on machines whose memory came in banks and a graphic larger than a bank could not be
  /// one object; a file being written here is not going into a bank, and one chunk is what a reader
  /// has least to reassemble. The sixteen-colour form is used throughout — the four-colour one packs
  /// a byte as four pixels' low bits in one nibble and their high bits in the other, which is what
  /// the Amstrad's hardware wanted and costs a picture twelve of its colours.
  /// </remarks>
  public static SymbOsGraphicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Width > MaxChunkExtent || image.Height > MaxChunkExtent)
      throw new ArgumentException(
        $"A SymbOS chunk states its size in words, so {image.Width}x{image.Height} exceeds the {MaxChunkExtent} either may be.",
        nameof(image));

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, SixteenColorPalette.ToArray());
    int width = image.Width, height = image.Height;
    var stride = (width + 1) >> 1;
    var data = new byte[WideHeaderSize + height * stride];

    data[0] = WideHeader;
    data[1] = WideHeaderKind;
    data[2] = (byte)stride;
    data[3] = (byte)(stride >> 8);
    data[4] = (byte)width;
    data[5] = (byte)(width >> 8);
    data[6] = (byte)height;
    data[7] = (byte)(height >> 8);

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      // Two pixels a byte, the left one in the high nibble.
      var index = indexed.PixelData[y * width + x] & 15;
      var at = WideHeaderSize + y * stride + (x >> 1);
      data[at] |= (byte)((x & 1) == 0 ? index << 4 : index);
    }

    return new() {
      Data = data,
      Width = width,
      Height = height,
      Chunks = [
        new() {
          DataOffset = WideHeaderSize,
          Stride = stride,
          Width = width,
          Height = height,
          Left = 0,
          Top = 0,
          IsWide = true,
        }
      ],
    };
  }
}
