using System;
using FileFormat.Core;

namespace FileFormat.MsxMig;

/// <summary>In-memory representation of an MSX MIG picture (.mig).</summary>
/// <remarks>
/// A compressed capture of what the video chip was doing, rather than a picture in any particular
/// mode. Inside the compression is a list of records: writes to the chip's own registers, a
/// palette, and finally the screen — and which of the machine's seven graphics modes the screen is
/// in has to be worked out from the register writes, exactly as the chip itself would have.
/// <para/>
/// That is also how interlacing is discovered: two of the register bits say the chip was showing
/// two pages alternately, and the file then carries a second screen behind the first.
/// </remarks>
public readonly record struct MsxMigFile
  : IImageFormatReader<MsxMigFile>, IImageToRawImage<MsxMigFile>,
    IImageFromRawImage<MsxMigFile>, IImageFormatWriter<MsxMigFile> {

  /// <summary>Bytes the unpacked records may occupy: two whole screens and their headers.</summary>
  public const int MaxUnpacked = 108800;

  static string IImageFormatMetadata<MsxMigFile>.PrimaryExtension => ".mig";
  static string[] IImageFormatMetadata<MsxMigFile>.FileExtensions => [".mig"];
  static MsxMigFile IImageFormatReader<MsxMigFile>.FromSpan(ReadOnlySpan<byte> data)
    => MsxMigReader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxMigFile>.ToBytes(MsxMigFile file) => MsxMigWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxMigFile>.VideoModes => [
    new("MSX2", [(256, 212), (512, 424)], [256]),
    new("MSX2+", [(256, 212), (512, 424)], [19268]),
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel.</summary>
  public byte[] Pixels { get; init; }

  public static RawImage ToRawImage(MsxMigFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.Pixels ?? new byte[file.Width * file.Height * 3],
  };

  /// <summary>Captures a picture as a screen 8 the chip could have been showing.</summary>
  /// <remarks>
  /// Screen 8 is the only one of the seven whose colours are fixed in hardware, so the file needs no
  /// palette record and the picture does not depend on a table it would also have to be trusted
  /// about. Its 256 colours are three bits of red and green and four unevenly spaced blues, which is
  /// what the picture is pulled onto.
  /// </remarks>
  public static MsxMigFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(MsxMigWriter.Columns, MsxMigWriter.Rows);
    var indexed = source.EnsureIndexed(PixelFormat.Indexed8, MsxMigWriter.Screen8Palette());
    var palette = indexed.Palette ?? [];
    var pixels = new byte[MsxMigWriter.Columns * MsxMigWriter.Rows * 3];

    for (var i = 0; i < indexed.PixelData.Length; ++i) {
      var entry = indexed.PixelData[i] * 3;
      if (entry + 2 >= palette.Length)
        continue;

      pixels[i * 3] = palette[entry];
      pixels[i * 3 + 1] = palette[entry + 1];
      pixels[i * 3 + 2] = palette[entry + 2];
    }

    return new() { Width = MsxMigWriter.Columns, Height = MsxMigWriter.Rows, Pixels = pixels };
  }
}
