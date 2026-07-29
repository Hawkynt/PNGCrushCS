using System;
using FileFormat.Core;

namespace FileFormat.DoomFlat;

/// <summary>In-memory representation of a Doom flat texture lump image.</summary>
public readonly record struct DoomFlatFile : IImageFormatReader<DoomFlatFile>, IImageToRawImage<DoomFlatFile>, IImageFromRawImage<DoomFlatFile>, IImageFormatWriter<DoomFlatFile> {

  internal const int FixedWidth = 64;
  internal const int FixedHeight = 64;
  internal const int FileSize = 4096;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFormatMetadata<DoomFlatFile>.PrimaryExtension => ".flat";
  static string[] IImageFormatMetadata<DoomFlatFile>.FileExtensions => [".flat"];
  static DoomFlatFile IImageFormatReader<DoomFlatFile>.FromSpan(ReadOnlySpan<byte> data) => DoomFlatReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DoomFlatFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256], _FixedPalettes)
  ];
  static byte[] IImageFormatWriter<DoomFlatFile>.ToBytes(DoomFlatFile file) => DoomFlatWriter.ToBytes(file);

  private static readonly FixedPalette[] _FixedPalettes = [
    new FixedPalette("DOOM PLAYPAL",
      0x000000, 0x1F170B, 0x170F07, 0x4B4B4B, 0xFFFFFF, 0x1B1B1B, 0x131313, 0x0B0B0B,
      0x070707, 0x2F371F, 0x232B0F, 0x171F07, 0x0F1700, 0x4F3B2B, 0x47331B, 0x3F2B17,
      0xFB7B7B, 0xEB6B6B, 0xDF5B5B, 0xD34B4B, 0xC73B3B, 0xBB2B2B, 0xAF2323, 0xA31B1B,
      0x971313, 0x870B0B, 0x7B0707, 0x6F0000, 0x630000, 0x570000, 0x4B0000, 0x3F0000,
      0xFFEBDF, 0xFFDFCB, 0xFFD7BB, 0xFFCBA7, 0xFFC397, 0xFFBB87, 0xF7AF7B, 0xE7A36F,
      0xD79767, 0xCB8B5B, 0xBB7F53, 0xAF7347, 0x9F6B3F, 0x8F5F37, 0x7F532B, 0x6F4723,
      0xFFFFB7, 0xF7F7AB, 0xEFEF9F, 0xE7E797, 0xDFDF8B, 0xD7D783, 0xCBCB77, 0xC3C36B,
      0xBBBB5F, 0xB3B357, 0x9F9F47, 0x8B8B37, 0x77772B, 0x67671F, 0x535313, 0x3F3F07,
      0xEFEFEF, 0xE7E7E7, 0xDFDFDF, 0xDBDBDB, 0xD3D3D3, 0xCBCBCB, 0xC7C7C7, 0xBFBFBF,
      0xB7B7B7, 0xB3B3B3, 0xABABAB, 0xA7A7A7, 0x9F9F9F, 0x979797, 0x939393, 0x8B8B8B,
      0x878787, 0x7F7F7F, 0x7B7B7B, 0x737373, 0x6B6B6B, 0x676767, 0x5F5F5F, 0x5B5B5B,
      0x535353, 0x4B4B4B, 0x474747, 0x3F3F3F, 0x3B3B3B, 0x333333, 0x2B2B2B, 0x272727,
      0x77FF6F, 0x6FEF67, 0x67DF5F, 0x5FCF57, 0x57BF4F, 0x4FAF47, 0x479F3F, 0x3F8F37,
      0x377F2F, 0x2F6F27, 0x275F1F, 0x1F4F17, 0x173F0F, 0x0F2F07, 0x071F00, 0x000F00,
      0xBBF3FB, 0xA7E3EB, 0x97D3DB, 0x83C3CB, 0x73B3BB, 0x63A3AB, 0x53939B, 0x47838B,
      0x37737B, 0x2B636B, 0x1F535B, 0x13434B, 0x0B333B, 0x07232B, 0x03171B, 0x00070B,
      0xFFFF73, 0xEBDB57, 0xD7BB43, 0xC39B2F, 0xAF7B1F, 0x9B5B13, 0x874307, 0x732B00,
      0xFFFFFF, 0xFFDBDB, 0xFFBBBB, 0xFF9B9B, 0xFF7B7B, 0xFF5B5B, 0xFF3B3B, 0xFF2323,
      0xFF0000, 0xE70000, 0xCF0000, 0xB70000, 0x9F0000, 0x870000, 0x6F0000, 0x570000,
      0xE7E7FF, 0xC7C7FF, 0xA7A7FF, 0x8B8BFF, 0x6F6FFF, 0x5353FF, 0x3737FF, 0x2323FF,
      0x0000FF, 0x0000E3, 0x0000CB, 0x0000B3, 0x00009B, 0x000083, 0x00006B, 0x000053,
      0xEFE7B7, 0xDBCFA7, 0xC7BB97, 0xB7A787, 0xA39377, 0x937F6B, 0x836F5B, 0x735F4F,
      0x635343, 0x534337, 0x47372B, 0x372B23, 0x2B1F17, 0x1F130F, 0x130B07, 0x070300,
      0xFFFF73, 0xEFDF53, 0xDFC33B, 0xCFA727, 0xBF8B13, 0xAF7300, 0x9F5F00, 0x8F4B00,
      0x7F3B00, 0x6F2B00, 0x5F1F00, 0x4F1300, 0x3F0B00, 0x2F0700, 0x1F0300, 0x0F0000,
      0x7B7BFF, 0x6B6BFF, 0x5B5BFF, 0x4B4BFF, 0x3B3BFF, 0x2B2BFF, 0x1B1BFF, 0x0B0BFF,
      0x0000FF, 0x0000E7, 0x0000CF, 0x0000B7, 0x00009F, 0x000087, 0x00006F, 0x000057,
      0x1F1F1F, 0x171717, 0x131313, 0x0F0F0F, 0x0B0B0B, 0x070707, 0x030303, 0x000000,
      0xFF9B43, 0xFFE3CB, 0xFFDBB7, 0xFFD3A3, 0xFFCB8F, 0xFFC37B, 0xFFB767, 0xFFAB57,
      0xFF9F47, 0xFF9337, 0xFF8727, 0xFF7B1B, 0xFF6F0F, 0xFB6307, 0xEF5B00, 0xE35300,
      0xA73B0B, 0x9F3307, 0x972B07, 0x8F2303, 0x871B03, 0x7B1700, 0x731300, 0x6B0B00,
      0xFFFF9F, 0xFFFF83, 0xFFFF6B, 0xFFFF4F, 0xFFFF37, 0xFFFF1F, 0xFFFF07, 0xEBDF00)
    // TODO: above palette has 256 entries; verify against canonical DOOM PLAYPAL (the last-row
    // black/red/gold colors that originally appeared here were an over-transcription).
  ];

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DoomFlatFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = _DefaultPalette[..],
      PaletteCount = 16,
    };
  }

  public static DoomFlatFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureIndexed(PixelFormat.Indexed8, _DefaultPalette);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
