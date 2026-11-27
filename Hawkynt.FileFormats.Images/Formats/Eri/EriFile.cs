using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Eri;

/// <summary>In-memory representation of an Entis Rasterized Image (.eri) or Moving Entis Image (.mei).</summary>
/// <remarks>
/// Leshade Entis' picture format for the EntisGLS game engines. The file is an EMC container — the
/// signature, then nested records named by eight characters — whose "Header  " record says how the
/// picture is shaped and coded and whose "Stream  " record holds the palette and one image record
/// per frame. An animation is the same file with more frames, later ones optionally coded as
/// differences from the frame before.
/// <para/>
/// The lossless codings are read: the standard format's run-length gamma code (level 1), its
/// 256-colour move-to-front code, the ERINA adaptive Huffman code (level 2) and the ERISA
/// arithmetic code (level 5), with every block arrangement and colour operation they define, in 8,
/// 16, 24 and 32 bits, paletted, grey, colour and colour with opacity, stored either way up.
/// </remarks>
[FormatMagicBytes([0x45, 0x6E, 0x74, 0x69, 0x73, 0x1A, 0x00, 0x00])]
public readonly record struct EriFile
  : IImageFormatReader<EriFile>, IImageToRawImage<EriFile>, IImageFromRawImage<EriFile>,
    IImageFormatWriter<EriFile>, IImageInfoReader<EriFile>, IMultiImageFileFormat<EriFile> {

  static string IImageFormatMetadata<EriFile>.PrimaryExtension => ".eri";
  static string[] IImageFormatMetadata<EriFile>.FileExtensions => [".eri", ".mei"];
  static EriFile IImageFormatReader<EriFile>.FromSpan(ReadOnlySpan<byte> data) => EriReader.FromSpan(data);
  static byte[] IImageFormatWriter<EriFile>.ToBytes(EriFile file) => EriWriter.ToBytes(file);
  static bool? IImageFormatMetadata<EriFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < 12 ? null : EriReader.HasSignature(header);
  static FormatCapability IImageFormatMetadata<EriFile>.Capabilities => FormatCapability.MultiImage;

  /// <summary>What the file contains as a whole, including its frame count and key-frame interval.</summary>
  public EriFileHeader FileHeader { get; init; }

  /// <summary>How the picture is shaped and coded.</summary>
  public EriInfoHeader Info { get; init; }

  /// <summary>256 entries of red, green and blue, for a paletted picture.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>The "cpyright" text.</summary>
  public string? Copyright { get; init; }

  /// <summary>The "descript" text, which may carry #-tagged fields such as a hot spot or resolution.</summary>
  public string? Description { get; init; }

  /// <summary>The order and timing frames are shown in, when the file says.</summary>
  public EriSequenceEntry[]? Sequence { get; init; }

  /// <summary>
  /// Every frame, top row first: blue, green, red and opacity for <see cref="EriColorType.Rgba"/>,
  /// blue, green, red for <see cref="EriColorType.Rgb"/>, one byte otherwise.
  /// </summary>
  public IReadOnlyList<byte[]> Frames { get; init; }

  public int Width => this.Info.Width;
  public int Height => this.Info.Rows;
  public EriColorType ColorType => EriReader.ColorTypeOf(this.Info);

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    try {
      var headers = EriReader.ReadHeaders(header, strict: false);
      var info = headers.Info!.Value;
      var colorType = EriReader.ColorTypeOf(info);
      return new(info.Width, info.Rows, info.BitsPerPixel, colorType.ToString(), CompressionName(info),
        (int)Math.Max(1, headers.FileHeader.FrameCount));
    } catch (Exception e) when (e is System.IO.InvalidDataException or NotSupportedException or ArgumentException) {
      return null;
    }
  }

  /// <summary>The coding a picture uses, as it is commonly called.</summary>
  public static string CompressionName(in EriInfoHeader info) {
    if (info.Transformation == EriInfoHeader.DctTransformation)
      return "ERI lossy DCT";
    if (info.Transformation == EriInfoHeader.LotTransformation)
      return "ERI lossy LOT";
    return info.Architecture switch {
      EriInfoHeader.RunLengthGamma => "ERI run-length gamma",
      EriInfoHeader.RunLengthHuffman => "ERINA",
      EriInfoHeader.Nemesis => "ERISA",
      EriInfoHeader.ArithmeticCode => "ERI arithmetic",
      _ => $"ERI {info.Architecture:X8}",
    };
  }

  public static int ImageCount(EriFile file) => file.Frames.Count;

  public static RawImage ToRawImage(EriFile file) => ToRawImage(file, 0);

  public static RawImage ToRawImage(EriFile file, int index) {
    ArgumentOutOfRangeException.ThrowIfNegative(index);
    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, file.Frames.Count);

    var info = file.Info;
    var colorType = EriReader.ColorTypeOf(info);
    var metadata = _Metadata(file);
    var pixels = file.Frames[index][..];

    return colorType switch {
      EriColorType.Rgba => new() { Width = info.Width, Height = info.Rows, Format = PixelFormat.Bgra32, PixelData = pixels, Metadata = metadata },
      EriColorType.Rgb => new() { Width = info.Width, Height = info.Rows, Format = PixelFormat.Bgr24, PixelData = pixels, Metadata = metadata },
      EriColorType.Gray => new() { Width = info.Width, Height = info.Rows, Format = PixelFormat.Gray8, PixelData = pixels, Metadata = metadata },
      _ => new() {
        Width = info.Width, Height = info.Rows, Format = PixelFormat.Indexed8, PixelData = pixels,
        Palette = (file.Palette ?? new byte[256 * 3])[..], PaletteCount = 256,
        AlphaTable = _ClipAlpha(info), Metadata = metadata,
      },
    };
  }

  // The clipped pixel is the palette index drawn as transparent.
  private static byte[]? _ClipAlpha(in EriInfoHeader info) {
    if ((info.FormatType & EriInfoHeader.UseClipping) == 0 || info.ClippedPixel is < 0 or > 255)
      return null;
    var alpha = Enumerable.Repeat((byte)255, 256).ToArray();
    alpha[info.ClippedPixel] = 0;
    return alpha;
  }

  private static ImageMetadata? _Metadata(EriFile file) {
    var entries = new List<TextMetadataEntry>();
    if (!string.IsNullOrEmpty(file.Description))
      entries.Add(new("Comment", file.Description));
    if (!string.IsNullOrEmpty(file.Copyright))
      entries.Add(new("Copyright", file.Copyright));
    return entries.Count == 0 ? null : new() { TextEntries = entries };
  }

  /// <summary>Stores a picture losslessly in the standard run-length gamma format, which every ERI decoder reads.</summary>
  /// <remarks>
  /// Pictures with opacity are written as 32-bit RGBA, grey ones as 8-bit grey, paletted ones with
  /// their palette, and everything else as 24-bit colour. Comment and copyright text are carried over.
  /// </remarks>
  public static EriFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1 || image.Width > EriReader.MaxDimension || image.Height > EriReader.MaxDimension)
      throw new ArgumentException($"ERI pictures are 1 to {EriReader.MaxDimension} pixels a side, not {image.Width}x{image.Height}.", nameof(image));

    string? description = null, copyright = null;
    foreach (var entry in image.Metadata?.TextEntries ?? [])
      if (entry.Keyword.Equals("Copyright", StringComparison.OrdinalIgnoreCase))
        copyright ??= entry.Text;
      else if (entry.Keyword is "Comment" or "Description" or "comment" or "description")
        description ??= entry.Text;

    EriColorType colorType;
    byte[] pixels;
    byte[]? palette = null;
    if (image.IsIndexed && !image.HasAlpha && image.PaletteCount <= 256) {
      var indexed = image.EnsureFormat(PixelFormat.Indexed8);
      colorType = EriColorType.Palette;
      pixels = indexed.PixelData[..(image.Width * image.Height)];
      palette = new byte[256 * 3];
      (indexed.Palette ?? []).AsSpan(0, Math.Min(indexed.Palette?.Length ?? 0, 256 * 3)).CopyTo(palette);
    } else if (image.Format is PixelFormat.Gray8) {
      colorType = EriColorType.Gray;
      pixels = image.PixelData[..(image.Width * image.Height)];
    } else if (image.HasAlpha) {
      colorType = EriColorType.Rgba;
      pixels = image.EnsureFormat(PixelFormat.Bgra32).PixelData[..(image.Width * image.Height * 4)];
    } else {
      colorType = EriColorType.Rgb;
      pixels = image.EnsureFormat(PixelFormat.Bgr24).PixelData[..(image.Width * image.Height * 3)];
    }

    return Create(image.Width, image.Height, colorType, [pixels], palette, description, copyright);
  }

  /// <summary>Builds a lossless picture, or an animation of frames that each stand alone.</summary>
  public static EriFile Create(int width, int height, EriColorType colorType, IReadOnlyList<byte[]> frames,
    byte[]? palette = null, string? description = null, string? copyright = null) {
    var (formatType, bits) = colorType switch {
      EriColorType.Rgba => (EriInfoHeader.RgbaImage, 32),
      EriColorType.Rgb => (EriInfoHeader.RgbImage, 24),
      EriColorType.Gray => (EriInfoHeader.GrayImage, 8),
      _ => (EriInfoHeader.RgbImage | EriInfoHeader.WithPalette, 8),
    };

    var info = new EriInfoHeader(
      EriInfoHeader.StandardVersion, EriInfoHeader.LosslessTransformation, EriInfoHeader.RunLengthGamma,
      formatType, width, -height, bits, 0, 0, 0, 0, colorType == EriColorType.Palette ? 0 : 4, 0, 0, 0);

    var contains = EriFileHeader.ContainImage | (colorType == EriColorType.Palette ? EriFileHeader.ContainPalette : 0);
    return new() {
      FileHeader = new(EriInfoHeader.StandardVersion, contains, 1, (uint)frames.Count, 0),
      Info = info,
      Palette = palette,
      Description = description,
      Copyright = copyright,
      Frames = frames,
    };
  }
}
