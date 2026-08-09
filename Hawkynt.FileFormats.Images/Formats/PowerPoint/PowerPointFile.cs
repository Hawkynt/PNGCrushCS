using System;
using FileFormat.Core;

namespace FileFormat.PowerPoint;

/// <summary>The picture inside a PowerPoint presentation or slide show (<c>.ppt</c>, <c>.pps</c>).</summary>
/// <remarks>
/// A presentation is a Microsoft compound document, and the pictures it was built from are stored in
/// it as whole JPEG and PNG files wrapped in OfficeArt <c>BLIP</c> records. XnView's catalogue names
/// the two extensions separately and its converter sends both to one and the same reader, so they
/// are one format under two names.
/// <para/>
/// That reader does not open the compound document's directory and never looks for the Pictures
/// stream by name. It checks the container signature, steps to offset 512 — the first byte behind
/// the container's own header — and from there walks eight-byte OfficeArt record headers: two bytes
/// of version and instance, two of record type, four of length, and then the record's data. Every
/// record is stepped over by the length it states, so a record that contains others is passed by
/// whole; the walk stops at a length of zero, at a length larger than the file, or at the end.
/// <para/>
/// Two record types end the walk: <c>0xF01D</c> at instance <c>0x46A</c>, which is a JPEG stored in
/// RGB, and <c>0xF01E</c> at instance <c>0x6E0</c>, which is a PNG. Both carry one sixteen-byte
/// checksum and a tag byte in front of the picture, so the picture begins seventeen bytes into the
/// record's data. The instance is part of the test rather than decoration: the same record types at
/// instance <c>0x46B</c>, <c>0x6E1</c> and so on carry a second checksum and put the picture two
/// bytes further along, and XnView reads none of them — which was confirmed by handing its converter
/// a file of each shape.
/// <para/>
/// Nothing else in the file is a picture as far as this is concerned, so a presentation of nothing
/// but text and shapes is refused rather than drawn empty.
/// </remarks>
public readonly record struct PowerPointFile
  : IImageFormatReader<PowerPointFile>, IImageToRawImage<PowerPointFile> {

  /// <summary>The eight bytes a Microsoft compound document opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

  /// <summary>Where the record walk begins, which is behind the container's own header.</summary>
  public const int ScanStart = 512;

  /// <summary>Version, instance, type and length.</summary>
  public const int RecordHeaderSize = 8;

  /// <summary>A checksum and a tag byte stand between a BLIP's header and the picture in it.</summary>
  public const int BlipPrefixSize = 17;

  /// <summary>The OfficeArt record type of a JPEG picture.</summary>
  public const ushort JpegBlipType = 0xF01D;

  /// <summary>The version and instance of the only JPEG shape read, which is RGB with one checksum.</summary>
  public const ushort JpegBlipVersionAndInstance = 0x46A0;

  /// <summary>The OfficeArt record type of a PNG picture.</summary>
  public const ushort PngBlipType = 0xF01E;

  /// <summary>The version and instance of the only PNG shape read, which carries one checksum.</summary>
  public const ushort PngBlipVersionAndInstance = 0x6E00;

  static string IImageFormatMetadata<PowerPointFile>.PrimaryExtension => ".ppt";
  static string[] IImageFormatMetadata<PowerPointFile>.FileExtensions => [".ppt", ".pps"];
  static PowerPointFile IImageFormatReader<PowerPointFile>.FromSpan(ReadOnlySpan<byte> data)
    => PowerPointReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<PowerPointFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>
  /// Abstains rather than claiming a compound document: every Office file in the world opens with
  /// the same eight bytes, and whether this one carries a picture is not known until it is walked.
  /// </summary>
  static bool? IImageFormatMetadata<PowerPointFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Signature.Length)
      return null;

    return header[..Signature.Length].SequenceEqual(Signature) ? null : false;
  }

  /// <summary>Image width in pixels, as the picture inside states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel, red first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PowerPointFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
