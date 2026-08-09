using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GeGenesis;

/// <summary>A GE Genesis 5.x image, the format the Visible Human "normal CT" slices are stored in (.fre).</summary>
/// <remarks>
/// XnView calls this one "Male Normal CT" after the dataset it met it in. The dataset is the National
/// Library of Medicine's Visible Human male, whose CT slices are named <c>cvm####.fre</c>, and the
/// files themselves are ordinary GE Genesis 5.x images: the scanner wrote them and the extension is
/// the dataset's, not the format's.
/// <para/>
/// The layout is the one described in David Clunie's Medical Image Format FAQ, part 4, under the GE
/// Genesis/Signa "ximg" format: a control header beginning with the four letters <c>IMGF</c> and
/// then big-endian 32-bit integers — the displacement to the pixel data at 4, the width at 8, the
/// height at 12, the depth in bits at 16 and a compression code at 20. The numbers are big-endian
/// because the scanner's console was a Sun 3.
/// <para/>
/// Only the uncompressed case is read, and it identifies itself: the bytes behind the header have to
/// be exactly the width times the height times the depth, to the byte. The perimeter-encoded and
/// DPCM-compressed variants the FAQ also describes are shorter than that and are refused by the same
/// test rather than by trusting the compression code, which the one file measured here states as 1
/// where the FAQ's list puts "as is" at 0. Refusing on the arithmetic rather than on the code means
/// a file that disagrees with itself is never drawn.
/// <para/>
/// Sixteen-bit samples are scaled to the full range by the largest sample the picture itself carries.
/// A CT slice occupies a few thousand of the 65,536 levels — the sample measured here runs to 2,848 —
/// so a reader that took the stored numbers as they stand would return a picture that is black. That
/// is also what XnView does with them: its eight-bit rendering of the sample is exactly
/// <c>sample * 255 / maximum</c> on every one of the 262,144 pixels, and the sixteen-bit picture read
/// here carries exactly that number as the top byte of every sample.
/// </remarks>
[FormatMagicBytes([0x49, 0x4D, 0x47, 0x46])]
public readonly record struct GeGenesisFile
  : IImageFormatReader<GeGenesisFile>, IImageToRawImage<GeGenesisFile>,
    IImageFromRawImage<GeGenesisFile>, IImageFormatWriter<GeGenesisFile> {

  /// <summary>The four letters a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "IMGF"u8;

  /// <summary>The fixed control header, which is where the identifier block starts in every file.</summary>
  public const int ControlHeaderSize = 156;

  /// <summary>What the largest sample in a picture is scaled to, 255 times 256.</summary>
  /// <remarks>
  /// Not 65,535. Scaling to 255 whole levels and then to 256 sublevels of each makes the top byte of
  /// every sample exactly <c>sample * 255 / maximum</c>, which is the eight-bit picture XnView draws,
  /// while the bottom byte keeps what an eight-bit rendering throws away. Scaling to 65,535 instead
  /// would leave a picture a level brighter than XnView's on a ninth of its pixels, for no gain.
  /// </remarks>
  public const int FullScale = 255 * 256;

  /// <summary>The fields this reader uses all stand inside the first 24 bytes.</summary>
  internal const int MinimumHeaderSize = 24;

  static string IImageFormatMetadata<GeGenesisFile>.PrimaryExtension => ".fre";
  static string[] IImageFormatMetadata<GeGenesisFile>.FileExtensions => [".fre"];
  static GeGenesisFile IImageFormatReader<GeGenesisFile>.FromSpan(ReadOnlySpan<byte> data) => GeGenesisReader.FromSpan(data);
  static byte[] IImageFormatWriter<GeGenesisFile>.ToBytes(GeGenesisFile file) => GeGenesisWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GeGenesisFile>.VideoModes => [
    new("Grey", [(IntegerRange.Any, IntegerRange.Any)], [256, 65536])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>How many bits a sample has, 8 or 16.</summary>
  public int Depth { get; init; }

  /// <summary>The samples as the file stores them, one row after another, big-endian when 16 bits.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GeGenesisFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    if (file.Depth == 8)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      };

    var count = file.Width * file.Height;
    var maximum = 0;
    for (var i = 0; i < count; ++i) {
      var sample = (file.PixelData[i * 2] << 8) | file.PixelData[i * 2 + 1];
      if (sample > maximum)
        maximum = sample;
    }

    var scaled = new byte[count * 2];
    if (maximum > 0)
      for (var i = 0; i < count; ++i) {
        var sample = (file.PixelData[i * 2] << 8) | file.PixelData[i * 2 + 1];
        var value = (int)((long)sample * FullScale / maximum);
        scaled[i * 2] = (byte)(value >> 8);
        scaled[i * 2 + 1] = (byte)value;
      }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray16,
      PixelData = scaled,
    };
  }

  /// <summary>Builds the uncompressed file a scanner would have written, at the same depth the picture has.</summary>
  public static GeGenesisFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format == PixelFormat.Gray8)
      return new() {
        Width = image.Width,
        Height = image.Height,
        Depth = 8,
        PixelData = image.PixelData[..],
      };

    var source = image.EnsureFormat(PixelFormat.Gray16);
    return new() {
      Width = source.Width,
      Height = source.Height,
      Depth = 16,
      PixelData = source.PixelData[..],
    };
  }
}
