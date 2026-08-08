using System;
using FileFormat.Core;

namespace FileFormat.CorelGallery;

/// <summary>The preview bitmap inside a Corel GALLERY clipart file (.bmf).</summary>
/// <remarks>
/// The drawing in one of these is Corel's own vector record stream and is not read here. What is read
/// is the thumbnail every one of them opens with: sixty-nine bytes of plain text — <c>@CorelBMF</c>,
/// the company name and a run of spaces, each line ended with a line feed and a carriage return, then
/// six bytes — and after them an ordinary Windows <c>BITMAPINFOHEADER</c>, its palette and its rows.
/// <para/>
/// The seven files there are to go by agree on all of it: the same sixty-nine bytes, the same 96 × 96
/// at eight bits, and a stated picture size of 9216 which is 96 × 96 to the byte. The header is read
/// where the format puts it rather than searched for, and it is refused when what it states does not
/// fit in the file — which is what keeps a file of the wrong kind from being drawn.
/// <para/>
/// Writing puts the same sixty-nine bytes back and the preview after them, which is a file this
/// reader opens by its own arithmetic. It is a preview and not a clipart file: the drawing is Corel's
/// own vector record stream, which is not read here and is not invented on the way out.
/// </remarks>
[FormatMagicBytes([(byte)'@', (byte)'C', (byte)'o', (byte)'r', (byte)'e', (byte)'l', (byte)'B', (byte)'M', (byte)'F'])]
public readonly record struct CorelGalleryFile
  : IImageFormatReader<CorelGalleryFile>, IImageToRawImage<CorelGalleryFile>,
    IImageFromRawImage<CorelGalleryFile>, IImageFormatWriter<CorelGalleryFile> {

  /// <summary>The nine bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic =>
    [(byte)'@', (byte)'C', (byte)'o', (byte)'r', (byte)'e', (byte)'l', (byte)'B', (byte)'M', (byte)'F'];

  /// <summary>Where the preview's <c>BITMAPINFOHEADER</c> starts, which is the same in every one of these.</summary>
  public const int PreviewOffset = 69;

  /// <summary>No thumbnail comes near this, and it keeps a false match cheap.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<CorelGalleryFile>.PrimaryExtension => ".bmf";
  static string[] IImageFormatMetadata<CorelGalleryFile>.FileExtensions => [".bmf"];
  static CorelGalleryFile IImageFormatReader<CorelGalleryFile>.FromSpan(ReadOnlySpan<byte> data)
    => CorelGalleryReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CorelGalleryFile>.VideoModes => [
    new("Preview", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))])
  ];

  /// <summary>The preview, already decoded by the bitmap reader.</summary>
  public RawImage Preview { get; init; }

  static byte[] IImageFormatWriter<CorelGalleryFile>.ToBytes(CorelGalleryFile file) => CorelGalleryWriter.ToBytes(file);

  public static RawImage ToRawImage(CorelGalleryFile file)
    => file.Preview ?? throw new InvalidOperationException("No preview was read.");

  public static CorelGalleryFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Preview = image };
  }
}
