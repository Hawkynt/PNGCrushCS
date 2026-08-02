using System;
using FileFormat.Core;

namespace FileFormat.Mag;

/// <summary>In-memory representation of a MAKIchan Graphics image.</summary>
/// <remarks>
/// This does not read the format. It is one of a family of readers that take bytes 0 to 5 as a size,
/// substitute a default when that looks wrong, and return whatever comes out — a MAG file states
/// <c>MAKI02</c> and is nothing like that, so the sample was reported as 16717 by 12848. It now
/// refuses rather than inventing a picture, which is the only honest thing it can do until the
/// format is read properly.
/// <para/>
/// What the layout is, established from the sample and checked against RECOIL and XnView, so the next
/// attempt need not start again:
/// <list type="bullet">
///   <item>Eight signature bytes, then a comment ending at the first <c>0x1A</c>. The header block
///     begins on the byte after it, and every offset below is relative to that byte.</item>
///   <item>In that block: a screen mode at 2 whose low bit chooses 16 or 256 colours; the drawn
///     region as four 16-bit values at 4, 6, 8 and 10, giving width and height as
///     <c>right - left + 1</c> and <c>bottom - top + 1</c>; then five 32-bit values — the offsets of
///     flag A, flag B, the size of flag B, and the offset and size of the pixel data.</item>
///   <item>The palette follows the block, three bytes an entry in green, red, blue order.</item>
///   <item>The sample resolves to 512 by 212, which both tools agree on, and header plus the pixel
///     offset plus its size is the file length exactly.</item>
///   <item>Flags are rebuilt one bit at a time from flag A: a set bit takes the next byte of flag B,
///     a clear bit takes zero, and the result is exclusive-ored with the flag one row above. There
///     are <c>width / 8</c> flag bytes a row, each holding two four-bit codes, and each code covers
///     two bytes of picture. Doing this consumes both streams to their last byte, which is the
///     strongest evidence the container is right.</item>
///   <item>A code of zero takes the next two bytes from the pixel data; anything else repeats two
///     bytes from earlier in the picture.</item>
///   <item>Nine codes are plain copies and their sources are settled. Asking only whether the source
///     region shows the same four pixels as the target, on every use, in the other tool's rendering
///     — which needs no decoding and so cannot be led astray by an earlier mistake — each of these
///     agrees on every single use: 2 repeats the two bytes two to the left, 3 six to the left, 5 the
///     row above, 7 and 8 two rows above, 10 and 11 four rows, 13 and 14 eight; where there are two,
///     they differ by two bytes horizontally.</item>
///   <item>Code 1 is the commonest by far, 15176 uses against a few hundred for most, and is
///     <b>not</b> settled. Two bytes to the left agrees on 94.64% of its uses and nothing does
///     better, so it is nearly that and not quite — an exception inside it, or another meaning
///     altogether.</item>
///   <item>Codes 4, 6, 9, 12 and 15 are <b>not</b> copies. Every horizontal offset to 128 bytes and
///     vertical to 32 rows was tried against each; the best reach 64% to 74% where the nine above
///     reach 100%. Whatever those five mean, it is not "repeat from here", and they and code 1 are
///     what is left to find.</item>
///   <item>With the nine settled and the six approximated, 87% of pixels come out right. That is not
///     shippable: a decoder wrong in one pixel in eight is the fault this reader is being fixed for,
///     so it refuses instead.</item>
/// </list>
/// <para/>
/// A caution for whoever picks this up: comparing decoded palette <em>indices</em> against another
/// tool's rendering gives nonsense here, because entries 0 and 1 are both black and the picture
/// cannot tell them apart. Compare colours.
/// </remarks>
public readonly record struct MagFile : IImageFormatReader<MagFile>, IImageToRawImage<MagFile>, IImageFromRawImage<MagFile>, IImageFormatWriter<MagFile> {

  internal const int HeaderSize = 32;

  static string IImageFormatMetadata<MagFile>.PrimaryExtension => ".mag";
  static string[] IImageFormatMetadata<MagFile>.FileExtensions => [".mag", ".mki"];
  static MagFile IImageFormatReader<MagFile>.FromSpan(ReadOnlySpan<byte> data) => MagReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MagFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])];
  static byte[] IImageFormatWriter<MagFile>.ToBytes(MagFile file) => MagWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MagFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static MagFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
