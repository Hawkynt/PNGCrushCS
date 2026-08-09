using System;
using FileFormat.Core;

namespace FileFormat.ChinonEs1000;

/// <summary>A picture off a Chinon ES-1000 digital camera (.cmt): a fixed size file holding the
/// camera's raw CCD readout, which the reader demosaics into a 500 by 241 colour picture.</summary>
/// <remarks>
/// The file is 125056 bytes and nothing else: a 128 byte file header opening with <c>COMET</c>, a
/// 512 byte camera header, then 243 lines of 512 raw CCD bytes. XnView's reader for the id
/// <c>cmt</c>, at 0x138df0 in nconvert 7.300, will not look at a file of any other length, compares
/// four bytes against <c>COMET</c> in its .rodata, seeks to 640, reads the 512 by 243 bytes, and
/// works from four buffers of that size — one byte plane and four word planes — which is exactly
/// the shape of YOSHIDA Hideki's public <c>cmttoppm.c</c>.
/// <para/>
/// It is not merely the same shape. The margins XnView subtracts are carried in its .rodata as the
/// four words 512, 243, 2 and 10 and the pair 1, 1, which are cmttoppm's COLUMNS, LINES,
/// LEFT_MARGIN, RIGHT_MARGIN and its TOP_MARGIN and BOTTOM_MARGIN, and 512-2-10 by 243-1-1 is the
/// 500 by 241 it reports. Its five steps stand as five functions called one after the other in the
/// same order, with the same constants 0.64, 0.58, 0.476, 0.299, 0.175, 1.5, 65536 and 0.5 sitting
/// in .rodata beside them.
/// <para/>
/// No .cmt file could be found anywhere, so fifteen were built here from known CCD values instead —
/// ramps, flat fields, chequerboards, gradients, single-column stripes, salt and pepper, rings and
/// several pseudo-random fields — and each was put through nconvert and through cmttoppm.c compiled
/// from source. The two agree on thirteen of the fifteen and differ on two, by fifteen samples out
/// of 361500 on one and thirty-seven on the other. The reason is precision and nothing else:
/// cmttoppm works the interpolation and the saturation in <c>float</c> and XnView works all of it in
/// <c>double</c>, which its instructions say plainly, and it even carries the square root of the
/// saturation as the full double 1.224744871391589 where the C rounds it to a float first. In a
/// step that divides by a neighbour's own estimate three times over, the last bit of a float
/// occasionally grows into a whole level. XnView is the standard here, so this follows XnView: the
/// arithmetic below is cmttoppm's algorithm carried out in double throughout, and it matches
/// nconvert on all fifteen, including both of the two where the C does not.
/// <para/>
/// The decode, in cmttoppm's order: the CCD is a colour filter array read one byte a cell, so a
/// horizontal estimate is seeded from the neighbours and then refined three times, a vertical
/// estimate is taken from the line above and below, the three of them are solved for red, green and
/// blue against the four filter patterns the parity of line and column selects, the colours are
/// scaled by 0.64, 0.58 and 1.00 and pushed out to a saturation of 1.5 at constant intensity, a
/// histogram of the extremes throws away the darkest and brightest three percent, and a gamma of
/// 0.5 maps what is left onto a byte.
/// <para/>
/// What refuses a foreign file is the length, which has to be 125056 exactly, together with the
/// signature.
/// </remarks>
[FormatMagicBytes([(byte)'C', (byte)'O', (byte)'M', (byte)'E', (byte)'T'])]
public readonly record struct ChinonEs1000File
  : IImageFormatReader<ChinonEs1000File>, IImageToRawImage<ChinonEs1000File> {

  /// <summary>The five bytes a file opens with; XnView compares only the first four of them.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'C', (byte)'O', (byte)'M', (byte)'E', (byte)'T'];

  /// <summary>The file header in front of the camera header.</summary>
  public const int FileHeaderSize = 128;

  /// <summary>The camera's own header, which nothing here reads.</summary>
  public const int CameraHeaderSize = 512;

  /// <summary>How many cells a CCD line holds.</summary>
  public const int CcdColumns = 512;

  /// <summary>How many lines the CCD has.</summary>
  public const int CcdLines = 243;

  /// <summary>The only length a file may have: 128 + 512 + 512 * 243.</summary>
  public const int FileSize = FileHeaderSize + CameraHeaderSize + CcdColumns * CcdLines;

  /// <summary>Cells cut off the left of every line.</summary>
  public const int LeftMargin = 2;

  /// <summary>Cells cut off the right of every line.</summary>
  public const int RightMargin = 10;

  /// <summary>Lines cut off the top.</summary>
  public const int TopMargin = 1;

  /// <summary>Lines cut off the bottom.</summary>
  public const int BottomMargin = 1;

  /// <summary>How wide the picture is once the margins are gone.</summary>
  public const int Width = CcdColumns - LeftMargin - RightMargin;

  /// <summary>How tall it is once the margins are gone.</summary>
  public const int Height = CcdLines - TopMargin - BottomMargin;

  static string IImageFormatMetadata<ChinonEs1000File>.PrimaryExtension => ".cmt";
  static string[] IImageFormatMetadata<ChinonEs1000File>.FileExtensions => [".cmt"];
  static ChinonEs1000File IImageFormatReader<ChinonEs1000File>.FromSpan(ReadOnlySpan<byte> data) => ChinonEs1000Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ChinonEs1000File>.VideoModes => [
    new("Chinon ES-1000", [(Width, Height)])
  ];

  /// <summary>The raw CCD readout, 512 cells a line and 243 lines.</summary>
  public byte[] CcdData { get; init; }

  public static RawImage ToRawImage(ChinonEs1000File file) {
    if (file.CcdData == null)
      throw new InvalidOperationException("No picture was read.");

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = ChinonEs1000Demosaic.ToRgb24(file.CcdData),
    };
  }
}
