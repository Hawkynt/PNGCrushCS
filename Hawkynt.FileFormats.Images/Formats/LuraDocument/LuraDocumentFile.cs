using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.LuraDocument;

/// <summary>
/// In-memory representation of a LuraDocument (<c>.ldf</c>) file: LuraTech's mixed raster content format, in which each
/// page is a JBIG mask choosing between a LuraWave-coded foreground and background.
/// </summary>
/// <remarks>
/// Every page decodes exactly as LuraTech's own decoder (the LuraDocument plug-in for IrfanView, built on LuraTech's
/// SDK) decodes it, on files that decoder's encoder wrote in each of its modes. The writer stores a colour or grey
/// page as a single full-size background layer and a black-and-white page as a mask alone. See <c>SPEC.md</c> beside
/// this file.
/// </remarks>
[FormatMimeType("image/x-luradocument")]
[VerifiedBy(ConformanceOracle.None)]
public sealed record LuraDocumentFile :
  IImageFormatReader<LuraDocumentFile>, IImageToRawImage<LuraDocumentFile>,
  IImageFromRawImage<LuraDocumentFile>, IImageFormatWriter<LuraDocumentFile>,
  IImageInfoReader<LuraDocumentFile>, IMultiImageFileFormat<LuraDocumentFile> {

  /// <summary>The resolution a page built from pixels gets.</summary>
  public const int DefaultDpi = 300;

  static string IImageFormatMetadata<LuraDocumentFile>.PrimaryExtension => ".ldf";
  static string[] IImageFormatMetadata<LuraDocumentFile>.FileExtensions => [".ldf"];
  static LuraDocumentFile IImageFormatReader<LuraDocumentFile>.FromSpan(ReadOnlySpan<byte> data) => LuraDocumentReader.FromSpan(data);
  static byte[] IImageFormatWriter<LuraDocumentFile>.ToBytes(LuraDocumentFile file) => LuraDocumentWriter.ToBytes(file);
  static FormatCapability IImageFormatMetadata<LuraDocumentFile>.Capabilities => FormatCapability.MultiImage;
  static bool? IImageFormatMetadata<LuraDocumentFile>.MatchesSignature(ReadOnlySpan<byte> header) => LuraDocumentContainer.Recognise(header);

  /// <summary>The pages in file order; never empty for a file that was read.</summary>
  public IReadOnlyList<LuraDocumentPage> Pages { get; init; } = [];

  /// <summary>Width of the first page.</summary>
  public int Width => this.Pages.Count > 0 ? this.Pages[0].Width : 0;

  /// <summary>Height of the first page.</summary>
  public int Height => this.Pages.Count > 0 ? this.Pages[0].Height : 0;

  /// <summary>Reads page count, size and kind of the first page from the directories, without decoding any layer.</summary>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    if (LuraDocumentContainer.Recognise(header) != true)
      return null;

    List<LuraDocumentReader.PageDirectories> pages;
    try {
      pages = LuraDocumentReader.Group(LuraDocumentContainer.Read(header.ToArray()));
    } catch (System.IO.InvalidDataException) {
      return null;
    }

    if (pages.Count == 0)
      return null;
    var first = pages[0];
    var masked = first.Page.Compression == LuraDocumentContainer.CompressionMask;
    var layers = new List<LuraDocumentDirectory>();
    if (masked && first.Foreground is { } f)
      layers.Add(f);
    if ((!masked || first.Foreground is not null) && first.Background is { } b)
      layers.Add(b);
    var bits = layers.Count == 0 ? 1 : layers.Exists(l => l.SamplesPerPixel == 3) ? 24 : 8;
    var mode = bits switch { 1 => "Bilevel", 8 => "Grey", _ => "Colour" };
    var compression = masked ? (layers.Count > 0 ? "JBIG mask, LuraWave layers" : "JBIG mask") : "LuraWave";
    return new(first.Page.Width, first.Page.Height, bits, mode, compression, pages.Count);
  }

  public static int ImageCount(LuraDocumentFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Pages.Count;
  }

  public static RawImage ToRawImage(LuraDocumentFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Pages.Count)
      throw new ArgumentOutOfRangeException(nameof(index));
    return file.Pages[index].ToRawImage();
  }

  /// <summary>The first page.</summary>
  public static RawImage ToRawImage(LuraDocumentFile file) => ToRawImage(file, 0);

  /// <summary>A one-page document; see <see cref="LuraDocumentPage.FromRawImage"/> for how the page kind is chosen.</summary>
  public static LuraDocumentFile FromRawImage(RawImage image) => new() { Pages = [LuraDocumentPage.FromRawImage(image)] };
}
