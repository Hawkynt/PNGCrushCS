using System;
using FileFormat.Core;

namespace FileFormat.AtariPicture;

/// <summary>In-memory representation of an APAC "Any Point, Any Color" picture (.apa, .apc, .plm).</summary>
/// <remarks>
/// The Atari's GTIA offers sixteen luminances in one graphics mode and sixteen hues in another, and
/// never both at once. APAC shows the two modes on alternate scanlines fast enough that they merge:
/// a hue row and a luminance row become one line of colour, and the machine gains all 256 shades it
/// can otherwise only pick sixteen of.
/// <para/>
/// Each stored row holds both halves — forty bytes of hue then forty of luminance — and covers two
/// scanlines. The odd one takes the luminance as stored; the even one has none of its own and
/// borrows the average of its neighbours, which is why an APAC picture is sharp in colour and soft
/// in brightness.
/// </remarks>
public readonly record struct AtariPictureFile
  : IImageFormatReader<AtariPictureFile>, IImageToRawImage<AtariPictureFile>,
    IImageFromRawImage<AtariPictureFile>, IImageFormatWriter<AtariPictureFile> {

  /// <summary>Displayed width.</summary>
  public const int Width = 320;

  /// <summary>Displayed height.</summary>
  public const int Height = 192;

  /// <summary>Stored rows; each covers two scanlines.</summary>
  public const int SourceRows = Height / 2;

  /// <summary>Logical pixels across a stored row; each covers four screen pixels.</summary>
  public const int LogicalWidth = 80;

  /// <summary>Bytes one half of a stored row occupies, at two logical pixels per byte.</summary>
  public const int HalfStride = LogicalWidth / 2;

  /// <summary>Bytes one stored row occupies: the hue half then the luminance half.</summary>
  public const int RowStride = HalfStride * 2;

  /// <summary>Offset of the hue half within a stored row.</summary>
  public const int HueOffset = 0;

  /// <summary>Offset of the luminance half within a stored row.</summary>
  public const int LuminanceOffset = HalfStride;

  /// <summary>Size of the picture data.</summary>
  public const int FileSize = RowStride * SourceRows;

  /// <summary>The size some files pad out to.</summary>
  public const int PaddedFileSize = 7720;

  /// <summary>
  /// The size the .mga variant comes in, which is the same picture with a longer trailer.
  /// </summary>
  public const int TrailedFileSize = 7856;

  static string IImageFormatMetadata<AtariPictureFile>.PrimaryExtension => ".apc";
  static string[] IImageFormatMetadata<AtariPictureFile>.FileExtensions => [".apc", ".apa", ".plm", ".aps", ".mga", ".pls"];
  static AtariPictureFile IImageFormatReader<AtariPictureFile>.FromSpan(ReadOnlySpan<byte> data) => AtariPictureReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariPictureFile>.ToBytes(AtariPictureFile file) => AtariPictureWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariPictureFile>.VideoModes => [
    new("APAC", [(Width, Height)], [256])
  ];

  /// <summary>The picture data, hue and luminance interleaved by row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Whether the hue half comes before the luminance half within a stored row.
  /// </summary>
  /// <remarks>
  /// APAC puts the hue first; the .mga variant puts the luminance first. Nothing in the bytes says
  /// which, and the two are the same length, so it comes from the extension by way of the file
  /// size. Reading one as the other gives a picture with the right shape whose colour and
  /// brightness have swapped roles.
  /// </remarks>
  public bool HueFirst { get; init; }

  /// <summary>Reads one nibble; each covers four screen pixels, high half of a byte first.</summary>
  private static int _Nibble(ReadOnlySpan<byte> data, int rowOffset, int logicalX) {
    var index = rowOffset + (logicalX >> 1);
    if (index >= data.Length)
      return 0;

    return (logicalX & 1) == 0 ? data[index] >> 4 : data[index] & 15;
  }

  public static RawImage ToRawImage(AtariPictureFile file) {
    var data = file.PixelData ?? [];

    // One GTIA colour byte per screen pixel: hue in the high nibble, luminance in the low one.
    var frame = new byte[Width * Height];

    var hueHalf = file.HueFirst ? HueOffset : LuminanceOffset;
    var luminanceHalf = file.HueFirst ? LuminanceOffset : HueOffset;

    // The odd scanlines carry the stored luminance.
    for (var row = 0; row < SourceRows; ++row)
    for (var x = 0; x < Width; ++x)
      frame[(row * 2 + 1) * Width + x] = (byte)_Nibble(data, row * RowStride + luminanceHalf, x >> 2);

    for (var row = 0; row < SourceRows; ++row) {
      var y = row * 2;
      for (var x = 0; x < Width; ++x) {
        var hue = (byte)(_Nibble(data, row * RowStride + hueHalf, x >> 2) << 4);

        // An even scanline stores no luminance of its own, so it takes the average of the two
        // around it — the top row having nothing above it, and the bottom nothing below.
        var above = y == 0 ? 0 : frame[(y - 1) * Width + x] & 15;
        var below = y == Height - 1 ? 0 : frame[(y + 1) * Width + x] & 15;
        frame[y * Width + x] = (byte)(hue | ((above + below) >> 1));

        if (y < Height - 1)
          frame[(y + 1) * Width + x] = (byte)(hue | (frame[(y + 1) * Width + x] & 15));
      }
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = frame,
      Palette = Atari8BitGraphics.CreatePalette(),
      PaletteCount = 256,
    };
  }

  public static AtariPictureFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.Palette;
    var data = new byte[FileSize];

    // A stored row owns two scanlines but only one hue and one luminance, so each is taken from
    // the scanline that actually carries it: the hue from the pair, the luminance from the odd one.
    for (var row = 0; row < SourceRows; ++row)
    for (var logicalX = 0; logicalX < LogicalWidth; ++logicalX) {
      var x = logicalX * 4;
      var hue = _NearestColor(bgra.PixelData, gtia, (row * 2) * Width + x) >> 4;
      var luminance = _NearestColor(bgra.PixelData, gtia, (row * 2 + 1) * Width + x) & 15;

      // Writing always produces the APAC order, which is what our primary extension names.
      _WriteNibble(data, row * RowStride + HueOffset, logicalX, hue);
      _WriteNibble(data, row * RowStride + LuminanceOffset, logicalX, luminance);
    }

    return new() { PixelData = data, HueFirst = true };
  }

  private static void _WriteNibble(byte[] data, int rowOffset, int logicalX, int value) {
    var index = rowOffset + (logicalX >> 1);
    data[index] |= (byte)((logicalX & 1) == 0 ? value << 4 : value);
  }

  /// <summary>The GTIA colour byte closest to a pixel.</summary>
  private static int _NearestColor(ReadOnlySpan<byte> bgra, ReadOnlySpan<byte> gtia, int pixel) {
    int red = bgra[pixel * 4 + 2], green = bgra[pixel * 4 + 1], blue = bgra[pixel * 4];
    var best = 0;
    var bestDistance = int.MaxValue;

    for (var i = 0; i < 256; ++i) {
      int dr = gtia[i * 3] - red, dg = gtia[i * 3 + 1] - green, db = gtia[i * 3 + 2] - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
    }

    return best;
  }
}
