using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.IbmKips;

/// <summary>In-memory representation of an IBM KIPS greyscale picture (.kps).</summary>
/// <remarks>
/// Eight bytes reading <c>DFIMAG00</c>, then the height and the width as little-endian words — that
/// way round — a word this does not use, and padding out to 32. After that one byte a pixel, rows
/// tight, and the byte is the shade.
/// <para/>
/// The size being height-then-width rather than the other way about is the only thing here worth
/// being careful of; a 320 by 200 picture states 200 first.
/// </remarks>
public readonly record struct IbmKipsFile
  : IImageFormatReader<IbmKipsFile>, IImageToRawImage<IbmKipsFile>,
    IImageFromRawImage<IbmKipsFile>, IImageFormatWriter<IbmKipsFile> {

  /// <summary>The eight bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "DFIMAG00"u8;

  /// <summary>Where the picture starts; the header is padded out to here.</summary>
  public const int HeaderSize = 32;

  internal const int HeightAt = 8, WidthAt = 10;

  static string IImageFormatMetadata<IbmKipsFile>.PrimaryExtension => ".kps";
  static string[] IImageFormatMetadata<IbmKipsFile>.FileExtensions => [".kps"];
  static IbmKipsFile IImageFormatReader<IbmKipsFile>.FromSpan(ReadOnlySpan<byte> data) => IbmKipsReader.FromSpan(data);
  static byte[] IImageFormatWriter<IbmKipsFile>.ToBytes(IbmKipsFile file) => IbmKipsWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<IbmKipsFile>.VideoModes => [
    new("Greyscale", [(new IntegerRange(1, ushort.MaxValue), new IntegerRange(1, ushort.MaxValue))], [256])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The bytes between the size and the picture, kept so writing one back preserves them.</summary>
  public byte[] Header { get; init; }

  /// <summary>One shade a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(IbmKipsFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Gray8,
    PixelData = (file.PixelData ?? [])[..],
  };

  public static IbmKipsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Header = new byte[HeaderSize],
      PixelData = PixelConverter.Convert(image, PixelFormat.Gray8).PixelData[..],
    };
  }
}
