using System;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.FlashImage;

/// <summary>A Flash Image picture (.fi): a twenty byte big-endian header and a payload that is
/// either a zlib stream holding a palette and eight bit indices, or a JPEG stream.</summary>
/// <remarks>
/// Nothing published describes this name, so the layout was taken out of XnView's own converter,
/// whose reader for the id <c>fi</c> stands at 0x1d9360 in nconvert 7.300, and then confirmed by
/// building files and asking that converter to read them back.
/// <para/>
/// The header is four bytes of signature <c>09 43 22 13</c>, then big-endian words: the width, the
/// height and a mode. Two more fields follow that the reader steps over — a longword at offset ten
/// and a longword at offset sixteen — with a word at offset fourteen between them that says how
/// many palette entries the payload carries. The header is twenty bytes and the payload starts
/// straight behind it.
/// <para/>
/// The mode selects the payload. Modes one and two send the reader 0x24c bytes past the ten bytes
/// it has read, so the JPEG stream stands at offset 598, and the picture's size then comes from
/// that stream and not from the header. Every other mode — nought and three upwards alike, the
/// reader does not look further — means a zlib stream, and that was the thing worth identifying:
/// the three entry points the reader drives are <c>inflateInit_</c>, <c>inflate</c> and
/// <c>inflateEnd</c>, called with a 112 byte block that is a <c>z_stream</c> laid out exactly as
/// zlib lays one out on this machine — input pointer, input count, output pointer, output count,
/// then the allocator pair and the opaque word — and with the version string "1.3.2". It is
/// neither LZW nor an LZ77 of its own; it is stock deflate under a zlib wrapper.
/// <para/>
/// The inflated bytes are the palette first, three bytes an entry in red, green, blue order and as
/// many entries as the header's word says, and the rows behind it, eight bits a pixel with each row
/// padded up to a multiple of four bytes. The converter takes the picture as one plane of eight
/// bits and always installs 256 entries whatever the header's count says, reading them straight off
/// the front of the inflated bytes; a file whose count is short and whose indices are not therefore
/// gets colours out of its own rows, which was checked by giving a four colour file the indices 4
/// and 5 and watching those two come back as the first row's bytes.
/// <para/>
/// Which fields are which was pinned by changing one at a time: the longwords at 10 and at 16 can
/// be filled with anything without the picture changing, the word at 14 shifts every row when it is
/// wrong, and modes 0, 3, 4 and 65535 all take the same branch.
/// <para/>
/// What refuses a foreign file is the signature, which is four bytes and specific. A file named
/// SURPRISE.FI that XnView will not touch opens with <c>FTC</c> and is an Iterated Systems fractal
/// transform file, an unrelated format that happens to share the extension; it is refused here for
/// the same reason XnView refuses it, the first four bytes.
/// </remarks>
[FormatMagicBytes([0x09, 0x43, 0x22, 0x13])]
public readonly record struct FlashImageFile
  : IImageFormatReader<FlashImageFile>, IImageToRawImage<FlashImageFile>,
    IImageFromRawImage<FlashImageFile>, IImageFormatWriter<FlashImageFile> {

  /// <summary>The four bytes a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x09, 0x43, 0x22, 0x13];

  /// <summary>The fixed header, up to and including the longword at offset sixteen.</summary>
  public const int HeaderSize = 20;

  /// <summary>Where a JPEG payload stands: ten bytes of header read, then 0x24c skipped.</summary>
  public const int JpegPayloadOffset = 10 + 0x24c;

  /// <summary>The mode the palette branch is written with; anything but one and two takes it.</summary>
  public const int IndexedMode = 0;

  /// <summary>How many bytes a full palette takes, which is what the converter always installs
  /// whatever the header's count says.</summary>
  public const int FullPaletteBytes = 256 * 3;

  static string IImageFormatMetadata<FlashImageFile>.PrimaryExtension => ".fi";
  static string[] IImageFormatMetadata<FlashImageFile>.FileExtensions => [".fi"];
  static FlashImageFile IImageFormatReader<FlashImageFile>.FromSpan(ReadOnlySpan<byte> data) => FlashImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<FlashImageFile>.ToBytes(FlashImageFile file) => FlashImageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FlashImageFile>.VideoModes => [
    new("Palette", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>The header's mode word, which picks the payload.</summary>
  public int Mode { get; init; }

  /// <summary>The palette, three bytes an entry in red, green, blue order.</summary>
  public byte[] Palette { get; init; }

  /// <summary>How many entries the palette holds.</summary>
  public int PaletteCount { get; init; }

  /// <summary>One index a pixel, one row after another, with the row padding taken off.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The JPEG stream a mode one or two file carries, or null for a palette file.</summary>
  public byte[]? JpegData { get; init; }

  /// <summary>How wide a row of indices is inside the payload: the width rounded up to four.</summary>
  public static int RowStride(int width) => (width + 3) & ~3;

  public static RawImage ToRawImage(FlashImageFile file) {
    if (file.JpegData != null)
      return JpegFile.ToRawImage(JpegReader.FromBytes(file.JpegData));

    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    var palette = new byte[FullPaletteBytes];
    file.Palette.AsSpan(0, Math.Min(file.Palette.Length, palette.Length)).CopyTo(palette);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = 256,
    };
  }

  /// <summary>Builds the palette form, which is the one this writes.</summary>
  public static FlashImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var source = image.EnsureFormat(PixelFormat.Indexed8);
    var count = Math.Min(Math.Max(source.PaletteCount, 1), 256);
    var palette = new byte[count * 3];
    source.Palette?.AsSpan(0, Math.Min(source.Palette.Length, palette.Length)).CopyTo(palette);

    return new() {
      Width = source.Width,
      Height = source.Height,
      Mode = IndexedMode,
      Palette = palette,
      PaletteCount = count,
      PixelData = source.PixelData[..],
    };
  }
}
