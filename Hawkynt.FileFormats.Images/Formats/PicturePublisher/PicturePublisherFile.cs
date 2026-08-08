using System;
using FileFormat.Core;

namespace FileFormat.PicturePublisher;

/// <summary>In-memory representation of a Micrografx Picture Publisher 5 document.</summary>
/// <remarks>
/// The document is a canvas and a stack of objects, and the picture is what the stack composites to.
/// A reader that returns the first raster in the file returns the base image, which in the one sample
/// here is a blank white rectangle the size of the page — a plausible-looking answer that is not the
/// picture. So the objects are composited in the order the file lists them, each into the rectangle
/// its own header states.
/// </remarks>
public readonly record struct PicturePublisherFile
  : IImageFormatReader<PicturePublisherFile>, IImageToRawImage<PicturePublisherFile> {

  /// <summary>The six bytes the document opens with.</summary>
  internal static ReadOnlySpan<byte> Signature => "PPUBII"u8;

  /// <summary>Signature, version, the canvas and its resolution.</summary>
  internal const int HeaderSize = 48;

  static string IImageFormatMetadata<PicturePublisherFile>.PrimaryExtension => ".pp5";
  static string[] IImageFormatMetadata<PicturePublisherFile>.FileExtensions => [".pp5"];

  static bool? IImageFormatMetadata<PicturePublisherFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Signature.Length && header[..Signature.Length].SequenceEqual(Signature) ? true : false;

  static PicturePublisherFile IImageFormatReader<PicturePublisherFile>.FromSpan(ReadOnlySpan<byte> data)
    => PicturePublisherReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<PicturePublisherFile>.VideoModes
    => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])];

  /// <summary>Canvas width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Canvas height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Pixels per inch the document states for both axes.</summary>
  public int Resolution { get; init; }

  /// <summary>How many objects the document holds, the base image included.</summary>
  public int ObjectCount { get; init; }

  /// <summary>The composited canvas, three bytes a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PicturePublisherFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData ?? [],
  };
}
