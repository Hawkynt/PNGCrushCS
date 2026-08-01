using System;
using FileFormat.Core;

namespace FileFormat.Sf3;

/// <summary>In-memory representation of a Simple File Format Family image (.sf3).</summary>
/// <remarks>
/// A deliberately plain container: a fixed magic, a thirty-byte header naming the size, the channel
/// count and the width of a sample, then the samples themselves with no compression and no palette.
/// <para/>
/// The header carries four bytes a writer fills with a checksum. Readers do not verify it — a file
/// with those bytes altered still decodes — so it is written as zero here rather than guessed at,
/// which is honest about what this does and does not know.
/// </remarks>
public readonly record struct Sf3File
  : IImageFormatReader<Sf3File>, IImageToRawImage<Sf3File>,
    IImageFromRawImage<Sf3File>, IImageFormatWriter<Sf3File> {

  /// <summary>The ten bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [0x81, 0x53, 0x46, 0x33, 0x00, 0xE0, 0xD0, 0x0D, 0x0A, 0x0A];

  /// <summary>The format identifier that names an image rather than one of the family's other kinds.</summary>
  public const byte ImageFormatId = 3;

  /// <summary>Bytes before the samples.</summary>
  public const int HeaderSize = 30;

  /// <summary>Offset of the width.</summary>
  public const int WidthOffset = 16;

  /// <summary>Offset of the channel count.</summary>
  public const int ChannelsOffset = 28;

  /// <summary>Offset of the sample format, whose low nibble is the bytes a sample takes.</summary>
  public const int SampleFormatOffset = 29;

  static string IImageFormatMetadata<Sf3File>.PrimaryExtension => ".sf3";
  static string[] IImageFormatMetadata<Sf3File>.FileExtensions => [".sf3"];
  static Sf3File IImageFormatReader<Sf3File>.FromSpan(ReadOnlySpan<byte> data) => Sf3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Sf3File>.ToBytes(Sf3File file) => Sf3Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Sf3File>.VideoModes => [
    new("SF3", [(IntegerRange.Any, IntegerRange.Any)], [1 << 24])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Samples a pixel carries: one grey, three colour, four with an alpha.</summary>
  public int Channels { get; init; }

  /// <summary>The picture, already widened to eight bits a sample.</summary>
  public byte[] Samples { get; init; }

  public static RawImage ToRawImage(Sf3File file) {
    var samples = file.Samples ?? [];
    var count = file.Width * file.Height;
    var format = file.Channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
    var stride = file.Channels == 4 ? 4 : 3;
    var pixels = new byte[count * stride];

    for (var i = 0; i < count; ++i) {
      var from = i * file.Channels;
      var to = i * stride;

      switch (file.Channels) {
        case 1: {
          var level = from < samples.Length ? samples[from] : (byte)0;
          pixels[to] = pixels[to + 1] = pixels[to + 2] = level;
          break;
        }

        case 4:
          for (var c = 0; c < 4; ++c)
            pixels[to + c] = from + c < samples.Length ? samples[from + c] : (byte)0;

          break;

        default:
          for (var c = 0; c < 3; ++c)
            pixels[to + c] = from + c < samples.Length ? samples[from + c] : (byte)0;

          break;
      }
    }

    return new() { Width = file.Width, Height = file.Height, Format = format, PixelData = pixels };
  }

  /// <summary>Builds a three-channel picture, which is what a colour image is here.</summary>
  public static Sf3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Channels = 3,
      Samples = rgb.PixelData,
    };
  }
}
