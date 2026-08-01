using System;
using FileFormat.Core;

namespace FileFormat.CiscoIp;

/// <summary>In-memory representation of a Cisco IP Phone image.</summary>
/// <remarks>
/// Not a binary format at all: it is a small XML document the phone fetches over HTTP, with the
/// picture inside it as hexadecimal text at two bits a pixel. The phone's screen is four shades of
/// grey, which is what those two bits are for.
/// <para/>
/// This used to be written as eighty bytes of binary header followed by 24-bit pixels — a shape
/// nothing on a phone or anywhere else would open.
/// </remarks>
public readonly record struct CiscoIpFile
  : IImageFormatReader<CiscoIpFile>, IImageToRawImage<CiscoIpFile>,
    IImageFromRawImage<CiscoIpFile>, IImageFormatWriter<CiscoIpFile> {

  /// <summary>Bits a pixel, giving the four shades the screen has.</summary>
  internal const int BitsPerPixel = 2;

  /// <summary>The element the document is wrapped in.</summary>
  internal const string RootElement = "CiscoIPPhoneImage";

  /// <summary>The four shades, darkest first, as the phone renders them.</summary>
  internal static readonly byte[] Palette = [255, 255, 255, 170, 170, 170, 85, 85, 85, 0, 0, 0];

  static string IImageFormatMetadata<CiscoIpFile>.PrimaryExtension => ".cip";
  static string[] IImageFormatMetadata<CiscoIpFile>.FileExtensions => [".cip"];
  static CiscoIpFile IImageFormatReader<CiscoIpFile>.FromSpan(ReadOnlySpan<byte> data) => CiscoIpReader.FromSpan(data);
  static byte[] IImageFormatWriter<CiscoIpFile>.ToBytes(CiscoIpFile file) => CiscoIpWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CiscoIpFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [4])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>What the phone shows above the picture.</summary>
  public string Title { get; init; }

  /// <summary>Where on the screen it is placed.</summary>
  public int LocationX { get; init; }

  public int LocationY { get; init; }

  /// <summary>One index a pixel, zero being the lightest of the four shades.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes one row of the packed picture takes.</summary>
  internal int Stride => (this.Width * BitsPerPixel + 7) / 8;

  public static RawImage ToRawImage(CiscoIpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = Palette[..],
    PaletteCount = 4,
  };

  public static CiscoIpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, Palette);

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      Title = string.Empty,
      PixelData = indexed.PixelData[..],
    };
  }
}
