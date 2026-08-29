using System;
using FileFormat.Core;

namespace FileFormat.Apx;

/// <summary>An Ability Photopaint image (.apx): a layered document with an uncompressed 32-bit picture behind its header.</summary>
/// <remarks>
/// Nothing published describes this format, so the layout comes from XnView's own reader, the
/// function its 567-entry format table pairs with the name <c>apx</c>. It reads 21 bytes and compares
/// them against one of two signatures, both exactly 21 bytes long and both naming the author:
/// <c>MXPaint-NickAvrionov</c> with its terminating zero, which is the earlier one, and
/// <c>MXPaintPro-NickAvrion</c>, which is the first 21 letters of the longer Pro name and is compared
/// without a terminator. The reader assembles them from eight-byte groups and checks the second one
/// down to its last letter, so a file with the twenty-first byte changed is refused.
/// <para/>
/// Behind the signature come three unsigned little-endian words this does not read, then two more,
/// <c>a</c> and <c>b</c>, whose product decides how far the reader steps: <c>a * b * 4 + 40</c> bytes.
/// Where it lands are the fields that matter — the dots per inch, which the converter reports on both
/// axes, the width, the height, and the number of layers, which may not be zero (a file that says
/// zero is refused with <c>APX : No layer !</c>). Two more words follow that are not read, then
/// sixteen bytes are stepped over, and then one record per layer: four words not read, a word giving
/// the length of a run of bytes to step over, and three more words not read.
/// <para/>
/// The picture stands behind the last layer record, uncompressed, four bytes to a pixel and
/// <c>width * 4</c> to a row. Which four bytes was settled by feeding the converter pixels of known
/// value: writing 10, 20, 30, 40 gets back red 40, green 30, blue 20 and alpha 10, so the order in the
/// file is alpha, blue, green, red. The converter reports the orientation as bottom left and a file
/// whose rows are stored bottom to top comes back with its rows the right way up, so the first row in
/// the file is the bottom row of the picture.
/// </remarks>
[FormatMagicBytes([
  (byte)'M', (byte)'X', (byte)'P', (byte)'a', (byte)'i', (byte)'n', (byte)'t', (byte)'-',
  (byte)'N', (byte)'i', (byte)'c', (byte)'k', (byte)'A', (byte)'v', (byte)'r', (byte)'i', (byte)'o', (byte)'n', (byte)'o', (byte)'v', 0x00
])]
[FormatMagicBytes([
  (byte)'M', (byte)'X', (byte)'P', (byte)'a', (byte)'i', (byte)'n', (byte)'t', (byte)'P', (byte)'r', (byte)'o', (byte)'-',
  (byte)'N', (byte)'i', (byte)'c', (byte)'k', (byte)'A', (byte)'v', (byte)'r', (byte)'i', (byte)'o', (byte)'n'
])]
public sealed class ApxFile : IImageFormatReader<ApxFile>, IImageToRawImage<ApxFile>, IImageFromRawImage<ApxFile>, IImageFormatWriter<ApxFile> {

  public const int SignatureSize = 21;
  public static ReadOnlySpan<byte> MagicPaint => "MXPaint-NickAvrionov\0"u8;
  public static ReadOnlySpan<byte> MagicPaintPro => "MXPaintPro-NickAvrion"u8;
  public const int BytesPerPixel = 4;
  public const int MaximumSide = 32768;

  static string IImageFormatMetadata<ApxFile>.PrimaryExtension => ".apx";
  static string[] IImageFormatMetadata<ApxFile>.FileExtensions => [".apx"];
  static ApxFile IImageFormatReader<ApxFile>.FromSpan(ReadOnlySpan<byte> data) => ApxReader.FromSpan(data);
  static byte[] IImageFormatWriter<ApxFile>.ToBytes(ApxFile file) => ApxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public int Resolution { get; init; }
  public int LayerCount { get; init; }
  public bool IsPro { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(ApxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.PixelData.Length == 0)
      throw new InvalidOperationException("No picture was read.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates the smallest legal one-layer APX document while preserving 32-bit RGBA pixels.</summary>
  public static ApxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > MaximumSide || image.Height is < 1 or > MaximumSide)
      throw new ArgumentException($"APX dimensions must be 1..{MaximumSide}; got {image.Width}x{image.Height}.", nameof(image));
    var rgba = image.EnsureFormat(PixelFormat.Rgba32);
    return new() {
      Width = rgba.Width,
      Height = rgba.Height,
      Resolution = 96,
      LayerCount = 1,
      IsPro = true,
      PixelData = rgba.PixelData[..],
    };
  }
}