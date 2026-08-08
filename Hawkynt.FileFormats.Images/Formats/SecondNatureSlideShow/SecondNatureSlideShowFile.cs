using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.SecondNatureSlideShow;

/// <summary>In-memory representation of a Second Nature slide show collection (.cat).</summary>
/// <remarks>
/// A collection opens with its own name in plain text, carries a title, a catalogue number and a
/// palette, and then a directory: a run of 32-bit little-endian words that alternate between where a
/// slide starts and how long it is. The first of them is where the slides begin, so how many there are
/// does not have to be stated — the directory fills the space between itself and them, eight bytes to
/// a slide.
/// <para/>
/// Each slide is a 42-byte record and then an ordinary JPEG. The record states the picture's width and
/// height twice, and both statements agree with the JPEG's own in all forty-four slides across the
/// eleven files there are — which is the check this reader keeps, because a directory that had been
/// read wrongly would not land on a JPEG whose size the record already knew.
/// <para/>
/// The arithmetic closes as well: every slide's start is the one before it plus that one's length, and
/// the last one ends on the last byte of the file.
/// <para/>
/// It does not write. What it read was a catalogue with a publisher's number on it.
/// </remarks>
[FormatMagicBytes([
  (byte)'S', (byte)'e', (byte)'c', (byte)'o', (byte)'n', (byte)'d', (byte)' ',
  (byte)'N', (byte)'a', (byte)'t', (byte)'u', (byte)'r', (byte)'e'
])]
public sealed class SecondNatureSlideShowFile
  : IImageFormatReader<SecondNatureSlideShowFile>, IImageToRawImage<SecondNatureSlideShowFile>,
    IMultiImageFileFormat<SecondNatureSlideShowFile> {

  /// <summary>The line every one of these opens with.</summary>
  public const string Signature = "Second Nature Software\r\nSlide Show\r\nCollection\r\n";

  /// <summary>Where the directory starts, which is the same in every one of these.</summary>
  public const int DirectoryOffset = 2277;

  /// <summary>A start and a length.</summary>
  public const int DirectoryEntrySize = 8;

  /// <summary>The record that stands ahead of each slide's JPEG.</summary>
  public const int SlideHeaderSize = 42;

  /// <summary>Where in that record the width and height are stated, twice over.</summary>
  public const int SlideSizeOffset = 12, SlideSizeRepeatOffset = 26;

  /// <summary>More slides than any collection holds, and it keeps a false match cheap.</summary>
  public const int MaxSlides = 4096;

  static string IImageFormatMetadata<SecondNatureSlideShowFile>.PrimaryExtension => ".cat";
  static string[] IImageFormatMetadata<SecondNatureSlideShowFile>.FileExtensions => [".cat"];
  static SecondNatureSlideShowFile IImageFormatReader<SecondNatureSlideShowFile>.FromSpan(ReadOnlySpan<byte> data)
    => SecondNatureSlideShowReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<SecondNatureSlideShowFile>.Capabilities => FormatCapability.MultiImage;

  /// <summary>The collection's title, as the header states it.</summary>
  public string Title { get; init; } = string.Empty;

  /// <summary>One entry per slide.</summary>
  public IReadOnlyList<SecondNatureSlide> Slides { get; init; } = [];

  public static int ImageCount(SecondNatureSlideShowFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Slides.Count;
  }

  public static RawImage ToRawImage(SecondNatureSlideShowFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Slides.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var slide = file.Slides[index];
    var picture = JpegFile.ToRawImage(JpegReader.FromBytes(slide.Jpeg));

    // The record said how big the picture is before the JPEG was opened. Disagreeing means the
    // directory was not read the way the collection meant it, so the picture is not handed back.
    if (picture.Width != slide.Width || picture.Height != slide.Height)
      throw new InvalidDataException($"A Second Nature slide states {slide.Width}x{slide.Height} and its JPEG is {picture.Width}x{picture.Height}.");

    return picture;
  }

  public static RawImage ToRawImage(SecondNatureSlideShowFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Slides.Count == 0)
      throw new InvalidDataException("A Second Nature collection holds no slides.");

    return ToRawImage(file, 0);
  }
}

/// <summary>One slide: the size its record states and the JPEG that follows it.</summary>
/// <param name="Width">The width the slide's record states.</param>
/// <param name="Height">The height the slide's record states.</param>
/// <param name="Jpeg">The JPEG, exactly as it stands in the file.</param>
public readonly record struct SecondNatureSlide(int Width, int Height, byte[] Jpeg);
