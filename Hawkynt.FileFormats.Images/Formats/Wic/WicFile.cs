using System;
using FileFormat.Core;

namespace FileFormat.Wic;

/// <summary>A J Wavelet Image Codec (JWIC) picture, the BeOS/Java wavelet format of Sasha Chukov's JWICTranslator.</summary>
/// <remarks>
/// <para>
/// JWIC is Joonas Lehtinen's GWIC research codec (zerotree coding of a wavelet pyramid, bit plane by
/// bit plane, through a JBIG QM arithmetic coder) carried to BeOS and to a Java applet in 1999–2000.
/// It is unrelated to Microsoft's Windows Imaging Component. Grey pictures are one plane; colour
/// pictures are NTSC YIQ with the chroma halved horizontally. The coding is lossy — the encoder
/// stops at a byte budget and every decoder reconstructs from single-precision wavelets — so
/// <see cref="PixelData"/> is the decoded picture and writing it again re-encodes it.
/// </para>
/// <para>
/// Both header generations are read and written: 1.0 (<c>1B 7A FB 30</c>) and 1.01
/// (<c>FA DE BA BE 01 01</c>). They differ in which channel comes first in a colour file, which this
/// type resolves, so <see cref="PixelData"/> is always R, G, B.
/// </para>
/// </remarks>
[FormatMagicBytes([0xFA, 0xDE, 0xBA, 0xBE, 0x01, 0x01])]
[FormatMagicBytes([0x1B, 0x7A, 0xFB, 0x30, 0x01, 0x00])]
[FormatMimeType("image/x-wic")]
public readonly record struct WicFile :
  IImageFormatReader<WicFile>, IImageToRawImage<WicFile>,
  IImageFromRawImage<WicFile>, IImageFormatWriter<WicFile>,
  IImageInfoReader<WicFile> {

  /// <summary>The translators' default bit budget: their quality slider's top setting, 1.5 bits per padded pixel.</summary>
  public const float DefaultBitsPerPixelBudget = 1.5f;

  /// <summary>The chroma budget coefficient both translators write.</summary>
  public const int DefaultChromaBudgetScale = 25;

  static string IImageFormatMetadata<WicFile>.PrimaryExtension => ".wic";
  static string[] IImageFormatMetadata<WicFile>.FileExtensions => [".wic"];
  static WicFile IImageFormatReader<WicFile>.FromSpan(ReadOnlySpan<byte> data) => WicReader.FromSpan(data);
  static byte[] IImageFormatWriter<WicFile>.ToBytes(WicFile file) => WicWriter.ToBytes(file);

  /// <summary>Both generations by magic and version, plus a colour model and transform the format defines.</summary>
  static bool? IImageFormatMetadata<WicFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (WicHeader.Identify(header) == null)
      return null;

    return header.Length < WicHeader.Size || (header[16] is WicHeader.Grey or WicHeader.Colour && header[17] <= 2);
  }

  /// <summary>Picture width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Picture height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The header generation; <see cref="WicGeneration.Version101"/> unless set otherwise.</summary>
  public WicGeneration Generation { get; init; } = WicGeneration.Version101;

  /// <summary>The wavelet; the translators default to <see cref="WicTransform.Biorthogonal97"/>.</summary>
  public WicTransform Transform { get; init; } = WicTransform.Biorthogonal97;

  /// <summary>True for a one-plane grey picture (<see cref="PixelData"/> is Gray8), false for colour (Rgb24).</summary>
  public bool IsGrayscale { get; init; }

  /// <summary>The header's resolution field, 0 when unspecified.</summary>
  public int Dpi { get; init; }

  /// <summary>The encoder's chroma budget coefficient: luma may spend (256 − 2c)/256 of the budget, the first chroma plane up to (256 − c)/256.</summary>
  public int ChromaBudgetScale { get; init; } = DefaultChromaBudgetScale;

  /// <summary>
  /// Bits per padded pixel the encoder may spend before it stops; a read file reports what its coder
  /// stream actually spent. Larger budgets keep more bit planes, up to the codec's limit of whole
  /// coefficients.
  /// </summary>
  public float BitsPerPixelBudget { get; init; } = DefaultBitsPerPixelBudget;

  /// <summary>Decoded samples: Gray8 for grey pictures, Rgb24 (R, G, B) for colour ones.</summary>
  public byte[] PixelData { get; init; }

  public WicFile() => this.PixelData = [];

  /// <summary>Size, depth and coding from the 32-byte header alone.</summary>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    WicHeader parsed;
    try {
      parsed = WicHeader.Parse(header);
    } catch (Exception exception) when (exception is System.IO.InvalidDataException or NotSupportedException) {
      return null;
    }

    return new(
      parsed.Width,
      parsed.Height,
      parsed.IsColour ? 24 : 8,
      parsed.IsColour ? "Rgb24" : "Gray8",
      $"JWIC zerotree ({parsed.Transform})");
  }

  public static RawImage ToRawImage(WicFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.IsGrayscale ? PixelFormat.Gray8 : PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  public static WicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);
    return new() {
      Width = image.Width,
      Height = image.Height,
      IsGrayscale = image.Format == PixelFormat.Gray8,
      PixelData = image.PixelData[..],
    };
  }
}
