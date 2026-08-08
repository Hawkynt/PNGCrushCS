using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.AtariHighResPage;

/// <summary>In-memory representation of a 640 by 400 Atari monochrome picture whose header is padding.</summary>
/// <remarks>
/// A resolution word of 2 — high resolution, as DEGAS numbers them — then a run of bytes that are
/// nought in the one sample, and last the picture: 640 by 400, one bit a pixel, a set bit ink.
/// <para/>
/// The picture is taken as the trailing 32000 bytes rather than from a fixed offset, because the
/// header's length is what is not established. The sample's is 331 bytes of which only the
/// resolution word is non-zero, and one file is not enough to say what the other 329 are for. The
/// trailing rule needs no such claim and lands on 34 for an ordinary DEGAS, which is where a DEGAS
/// picture starts, so it does not stop being right for the shorter form.
/// <para/>
/// Worth knowing if this is revisited: fitting the picture by sampled points alone puts it at 34
/// and at 2 as readily as at 331. Over all 256000 pixels those score 52 and 57 per cent against 100
/// for 331, so a candidate offset means nothing until the whole picture agrees.
/// <para/>
/// <c>.pg3</c> was claimed only by Atari Paintworks, which wants a signature this file does not
/// carry and a length of 32128.
/// </remarks>
public readonly record struct AtariHighResPageFile
  : IImageFormatReader<AtariHighResPageFile>, IImageToRawImage<AtariHighResPageFile>,
    IImageFromRawImage<AtariHighResPageFile>, IImageFormatWriter<AtariHighResPageFile> {

  public const int Width = 640;
  public const int Height = 400;
  public const int ColorCount = 2;

  /// <summary>Bytes the picture takes.</summary>
  public const int BitmapSize = Width * Height / 8;

  /// <summary>The resolution word this reads, which is how DEGAS numbers the monochrome mode.</summary>
  public const int HighResolution = 2;

  /// <summary>The header the sample carries, which is what the writer emits.</summary>
  public const int SampleHeaderSize = 331;

  /// <summary>The most header this accepts, so a longer file is not read as a shorter picture.</summary>
  public const int MaxHeaderSize = 512;

  static string IImageFormatMetadata<AtariHighResPageFile>.PrimaryExtension => ".pg3";
  static string[] IImageFormatMetadata<AtariHighResPageFile>.FileExtensions => [".pg3"];
  static AtariHighResPageFile IImageFormatReader<AtariHighResPageFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariHighResPageReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariHighResPageFile>.ToBytes(AtariHighResPageFile file)
    => AtariHighResPageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariHighResPageFile>.VideoModes => [
    new("High resolution", [(Width, Height)], [ColorCount])
  ];

  /// <summary>Whatever precedes the picture, kept so writing one back preserves it.</summary>
  public byte[] Header { get; init; }

  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AtariHighResPageFile file)
    => MonochromePage.Decode(file.PixelData ?? [], Width, Height, inkIsWhite: false);

  public static AtariHighResPageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var header = new byte[SampleHeaderSize];
    BinaryPrimitives.WriteUInt16BigEndian(header, HighResolution);

    return new() {
      Header = header,
      PixelData = MonochromePage.Encode(image.SampleTo(Width, Height), Width, Height, inkIsWhite: false),
    };
  }
}
