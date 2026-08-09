using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Arf;

/// <summary>An ARF picture (.arf): a big-endian header and eight bits a pixel behind it.</summary>
/// <remarks>
/// There are two unrelated formats under this name and this is not the one with a specification.
/// Axon's Raw Format, the one INDEC BioSystems' Imaging Workbench writes, opens with a byte-order
/// word and the letters <c>AR</c>, and its application note describes it fully — but XnView's ARF
/// reader refuses a file built to that note. What it wants is four bytes of
/// <c>BB BB BA AD</c>, and nothing published anywhere describes what writes them.
/// <para/>
/// So the layout here comes from asking XnView's own converter. Everything is big-endian: the four
/// bytes of signature, a version that has to be 2, the height, the width, a type code, a word this
/// does not read, the offset the picture stands at, and another word this does not read. Files were
/// built with type codes 0, 1 and 2 and all three were read; 3 was refused, as was a version of 1.
/// The picture is one byte a pixel and what the converter writes out of it is the bytes that went
/// in, exactly.
/// <para/>
/// What refuses a foreign file here is the signature, which is four bytes and specific, together
/// with the picture having to fit: the offset has to stand inside the file and the width times the
/// height has to be there behind it.
/// </remarks>
[FormatMagicBytes([0xBB, 0xBB, 0xBA, 0xAD])]
public readonly record struct ArfFile
  : IImageFormatReader<ArfFile>, IImageToRawImage<ArfFile>,
    IImageFromRawImage<ArfFile>, IImageFormatWriter<ArfFile> {

  /// <summary>The four bytes a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0xBB, 0xBB, 0xBA, 0xAD];

  /// <summary>The only version the reader accepts.</summary>
  public const int SupportedVersion = 2;

  /// <summary>Eight big-endian words before anything else.</summary>
  public const int HeaderSize = 32;

  /// <summary>The largest side the reader accepts.</summary>
  public const int MaximumSide = 16000;

  static string IImageFormatMetadata<ArfFile>.PrimaryExtension => ".arf";
  static string[] IImageFormatMetadata<ArfFile>.FileExtensions => [".arf"];
  static ArfFile IImageFormatReader<ArfFile>.FromSpan(ReadOnlySpan<byte> data) => ArfReader.FromSpan(data);
  static byte[] IImageFormatWriter<ArfFile>.ToBytes(ArfFile file) => ArfWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ArfFile>.VideoModes => [
    new("Grey", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>The type code the header carries, which is 0, 1 or 2 and changes nothing this reads.</summary>
  public int ImageType { get; init; }

  /// <summary>The samples, one byte a pixel, one row after another.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ArfFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Builds the grey picture, which is the only kind the format holds.</summary>
  public static ArfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var source = image.EnsureFormat(PixelFormat.Gray8);
    return new() {
      Width = source.Width,
      Height = source.Height,
      ImageType = 0,
      PixelData = source.PixelData[..],
    };
  }
}
