using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf;

/// <summary>In-memory representation of a PDF file's extracted raster images.</summary>
[FormatMagicBytes([0x25, 0x50, 0x44, 0x46])] // %PDF
[VerifiedBy(ConformanceOracle.ImageMagick, ConformanceOracle.Ghostscript)]
public sealed class PdfFile : IImageFormatReader<PdfFile>, IImageToRawImage<PdfFile>, IImageFromRawImage<PdfFile>, IImageFormatWriter<PdfFile>, IMultiImageFileFormat<PdfFile> {

  static string IImageFormatMetadata<PdfFile>.PrimaryExtension => ".pdf";
  static string[] IImageFormatMetadata<PdfFile>.FileExtensions => [".pdf"];
  static PdfFile IImageFormatReader<PdfFile>.FromSpan(ReadOnlySpan<byte> data) => PdfReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PdfFile>.Capabilities => FormatCapability.MultiImage;

  static bool? IImageFormatMetadata<PdfFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;

    return header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;
  }

  static byte[] IImageFormatWriter<PdfFile>.ToBytes(PdfFile file) => PdfWriter.ToBytes(file);

  private IReadOnlyList<PdfImage>? _images;
  private readonly Func<IReadOnlyList<PdfImage>>? _extractImages;
  private IReadOnlyList<PdfPage>? _pages;

  /// <summary>Builds a document in memory from its images, one page each, as <see cref="PdfWriter"/> lays them out.</summary>
  public PdfFile() { }

  internal PdfFile(PdfDocument document, Func<IReadOnlyList<PdfImage>> extractImages) {
    this.Document = document;
    this._extractImages = extractImages;
  }

  /// <summary>The document a file was read from, or null for one built in memory.</summary>
  internal PdfDocument? Document { get; }

  /// <summary>Why the page structure could not be read, when it could not and the images could.</summary>
  internal Exception? DocumentFailure { get; init; }

  /// <summary>
  /// The raster images embedded in the document as image XObjects, decoded to pixels: the pictures
  /// in it rather than its pages. Extracted on first use.
  /// </summary>
  public IReadOnlyList<PdfImage> Images {
    get => this._images ??= this._extractImages?.Invoke() ?? [];
    init => this._images = value;
  }

  /// <summary>The document's pages, in order. A document built in memory has one per image.</summary>
  public IReadOnlyList<PdfPage> Pages => this._pages ??= this._DescribePages();

  private IReadOnlyList<PdfPage> _DescribePages() {
    var pages = new List<PdfPage>();
    if (this.Document != null) {
      for (var i = 0; i < this.Document.Pages.Count; ++i) {
        var node = this.Document.Pages[i];
        pages.Add(new() { Index = i, MediaBox = node.MediaBox, CropBox = node.CropBox, Rotation = node.Rotate, UserUnit = node.UserUnit });
      }

      return pages;
    }

    for (var i = 0; i < this.Images.Count; ++i) {
      var box = new PdfBox(0, 0, this.Images[i].Width, this.Images[i].Height);
      pages.Add(new() { Index = i, MediaBox = box, CropBox = box });
    }

    return pages;
  }

  /// <summary>Renders the first page.</summary>
  public static RawImage ToRawImage(PdfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (ImageCount(file) == 0)
      throw new InvalidOperationException(file.Document == null ? "PDF contains no extractable images." : "PDF has no pages.");

    return ToRawImage(file, 0);
  }

  /// <summary>Creates a single-image PDF from a <see cref="RawImage"/>.</summary>
  public static PdfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);
    if (image.Format is not (PixelFormat.Rgb24 or PixelFormat.Gray8))
      throw new ArgumentException($"Expected Rgb24 or Gray8 but got {image.Format}.", nameof(image));

    var pdfImage = new PdfImage {
      Width = image.Width,
      Height = image.Height,
      BitsPerComponent = 8,
      ColorSpace = image.Format == PixelFormat.Rgb24 ? PdfColorSpace.DeviceRGB : PdfColorSpace.DeviceGray,
      PixelData = image.PixelData[..],
    };

    return new PdfFile { Images = [pdfImage] };
  }

  /// <summary>The number of pages.</summary>
  public static int ImageCount(PdfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.DocumentFailure is NotSupportedException refused)
      throw refused;

    return file.Pages.Count;
  }

  /// <summary>Renders a page at the default options: 72 dots per inch, annotations shown, at most 16 megapixels.</summary>
  /// <remarks>
  /// A document built in memory rather than read from bytes has no content streams; its pages are its
  /// images, each one page the size of the image, which is exactly how <see cref="PdfWriter"/> lays
  /// them out, so those come back as they went in.
  /// </remarks>
  public static RawImage ToRawImage(PdfFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Document == null) {
      if (file.DocumentFailure is NotSupportedException refused)
        throw refused;
      if ((uint)index >= (uint)file.Images.Count)
        throw new ArgumentOutOfRangeException(nameof(index));

      return file.Images[index].ToRawImage();
    }

    return RenderPage(file, index).Image;
  }

  /// <summary>Renders a page.</summary>
  /// <param name="file">The document, which must have been read from bytes.</param>
  /// <param name="index">The page, from zero.</param>
  /// <param name="options">Resolution, pixel cap and whether to draw annotations; the defaults when null.</param>
  /// <exception cref="NotSupportedException">The page uses something this renderer does not implement; the message names it.</exception>
  public static PdfPageRendering RenderPage(PdfFile file, int index, PdfRenderOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    options ??= PdfRenderOptions.Default;
    options.Validate();
    if (file.Document == null) {
      if (file.DocumentFailure is NotSupportedException refused)
        throw refused;
      throw new InvalidOperationException("Only a PDF read from bytes has pages to render.");
    }

    if ((uint)index >= (uint)file.Document.Pages.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return Rendering.PdfPageRenderer.Render(file.Document, file.Document.Pages[index], options);
  }
}
