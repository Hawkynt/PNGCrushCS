using System;
using FileFormat.Core;

namespace FileFormat.Mrw;

/// <summary>In-memory representation of a Minolta raw file (.mrw).</summary>
/// <remarks>
/// A block container, big-endian throughout. Four bytes of <c>\0MRM</c>, four giving the length of
/// everything before the sensor data, and then blocks of the same shape — a four-byte name and a
/// length — laid end to end. <c>\0PRD</c> gives the picture's shape, <c>\0WBG</c> the white balance
/// the camera metered, <c>\0TTW</c> a TIFF of ordinary EXIF, and what follows the last of them is
/// the sensor itself.
/// <para/>
/// The sensor data is not compressed and is not padded: twelve bits to a photosite, two photosites
/// to every three bytes, and the count of them the size in <c>\0PRD</c> asks for is exactly the
/// number of bytes the file has left. That is what says the blocks have been walked correctly.
/// <para/>
/// The array is a little larger than the picture — the camera reads a margin it does not show — and
/// the picture is its top-left corner. The mosaic starts on red, which was settled by demosaicing
/// each of the four possible phases and correlating the result against the preview the file carries
/// of the same scene: red-against-red beats red-against-blue for that phase alone, and the other
/// three either lose outright or transpose the two.
/// <para/>
/// The preview itself is only a quarter of the picture's width, so it is not what is drawn. It is
/// evidence about the sensor data, not a substitute for it.
/// </remarks>
public readonly record struct MrwFile : IImageFormatReader<MrwFile>, IImageToRawImage<MrwFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x00, (byte)'M', (byte)'R', (byte)'M'];

  /// <summary>The block naming the picture's shape.</summary>
  public static ReadOnlySpan<byte> PictureBlock => [0x00, (byte)'P', (byte)'R', (byte)'D'];

  /// <summary>The block naming the metered white balance.</summary>
  public static ReadOnlySpan<byte> WhiteBalanceBlock => [0x00, (byte)'W', (byte)'B', (byte)'G'];

  /// <summary>A block is a four-byte name and a four-byte length.</summary>
  public const int BlockHeaderSize = 8;

  /// <summary>The magic and the length of everything before the sensor data.</summary>
  public const int HeaderSize = 8;

  /// <summary>How much of the picture block this reads.</summary>
  public const int PictureBlockSize = 19;

  /// <summary>The only sample depth these store.</summary>
  public const int SupportedBitsPerSample = 12;

  static string IImageFormatMetadata<MrwFile>.PrimaryExtension => ".mrw";
  static string[] IImageFormatMetadata<MrwFile>.FileExtensions => [".mrw"];
  static MrwFile IImageFormatReader<MrwFile>.FromSpan(ReadOnlySpan<byte> data) => MrwReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MrwFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data in RGB24 interleaved order.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MrwFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }
}
