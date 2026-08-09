using System;
using FileFormat.Core;

namespace FileFormat.SonyPmp;

/// <summary>In-memory representation of a Sony Cyber-shot DSC-F1 picture (.pmp).</summary>
/// <remarks>
/// The DSC-F1 of 1996 was Sony's first Cyber-shot. It wrote a JPEG with a hundred and twenty-four
/// bytes of its own in front of it, and that is the whole of the format: strip the header and what
/// is left opens in anything.
/// <para/>
/// The header's fields are big-endian and are described on Fred Klingebiel's DSC-F1 page, which is
/// also what ExifTool's <c>Sony.pm</c> cites for its PMP table. The two this reader uses are the
/// length of the header at offset eight, which is always a hundred and twenty-four, and the length
/// of the JPEG behind it at offset twelve. The size the header states at offsets twenty-two and
/// twenty-four is not used: XnView ignores it too, and a header that disagrees with the JPEG comes
/// back at the JPEG's size from both.
/// <para/>
/// XnView will take a JPEG behind a prefix of any length up to about three hundred bytes, which
/// means it would also read a foreign file that happened to carry a JPEG near its start. This reader
/// does not: the header has to state a hundred and twenty-four, the JPEG has to begin exactly there,
/// and the length it states has to account for the rest of the file. That is stricter than XnView
/// and agrees with it on every file the camera wrote.
/// </remarks>
public readonly record struct SonyPmpFile : IImageFormatReader<SonyPmpFile>, IImageToRawImage<SonyPmpFile> {

  /// <summary>The one header length the format has.</summary>
  public const int HeaderSize = 124;

  /// <summary>Where the header states its own length.</summary>
  public const int HeaderSizeOffset = 8;

  /// <summary>Where it states how long the JPEG behind it is.</summary>
  public const int JpegLengthOffset = 12;

  /// <summary>What a JPEG opens with.</summary>
  public static ReadOnlySpan<byte> JpegStart => [0xFF, 0xD8, 0xFF];

  static string IImageFormatMetadata<SonyPmpFile>.PrimaryExtension => ".pmp";
  static string[] IImageFormatMetadata<SonyPmpFile>.FileExtensions => [".pmp"];
  static SonyPmpFile IImageFormatReader<SonyPmpFile>.FromSpan(ReadOnlySpan<byte> data) => SonyPmpReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<SonyPmpFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<SonyPmpFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize + 3)
      return null;

    var stated = (header[HeaderSizeOffset] << 24) | (header[HeaderSizeOffset + 1] << 16)
                 | (header[HeaderSizeOffset + 2] << 8) | header[HeaderSizeOffset + 3];

    return stated == HeaderSize && header.Slice(HeaderSize, JpegStart.Length).SequenceEqual(JpegStart);
  }

  /// <summary>Image width in pixels, as the JPEG behind the header states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel, red first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SonyPmpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
