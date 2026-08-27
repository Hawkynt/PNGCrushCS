using System;
using FileFormat.Core;

namespace FileFormat.Portrait;

/// <summary>A Portrait picture (.cvp): five hundred and twelve square, three planes, no header.</summary>
/// <remarks>
/// Nothing published describes this name, so the layout was taken from XnView's own converter, whose
/// reader for it does exactly two things: it refuses any file whose length is not 0xC0000 bytes, and
/// it then reads three planes of 512 rows of 512 bytes. There is no signature, no dimension field and
/// no version — the length is the whole of the header, which is why this reader cannot be offered for
/// content sniffing and is reached by its extension alone.
/// <para/>
/// The plane order was settled by building a file whose three planes carried three different ramps and
/// asking the converter for the pixels back: the first plane came out as red, the second as green and
/// the third as blue, and the bytes were the ones that went in.
/// </remarks>
public readonly record struct PortraitFile
  : IImageFormatReader<PortraitFile>, IImageToRawImage<PortraitFile>, IImageFromRawImage<PortraitFile>, IImageFormatWriter<PortraitFile> {

  /// <summary>The only side the format has.</summary>
  public const int Side = 512;

  /// <summary>How many bytes one plane takes.</summary>
  public const int PlaneSize = Side * Side;

  /// <summary>The only length a file may have.</summary>
  public const int FileSize = PlaneSize * 3;

  static string IImageFormatMetadata<PortraitFile>.PrimaryExtension => ".cvp";
  static string[] IImageFormatMetadata<PortraitFile>.FileExtensions => [".cvp"];
  static PortraitFile IImageFormatReader<PortraitFile>.FromSpan(ReadOnlySpan<byte> data)
    => PortraitReader.FromSpan(data);
  static byte[] IImageFormatWriter<PortraitFile>.ToBytes(PortraitFile file) => PortraitWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PortraitFile>.VideoModes => [
    new("Portrait", [(Side, Side)], [16777216])
  ];

  /// <summary>The three planes as they stand in the file: all of red, then all of green, then all of blue.</summary>
  public byte[] PlaneData { get; init; }

  public static RawImage ToRawImage(PortraitFile file) {
    var planes = file.PlaneData;
    if (planes == null || planes.Length != FileSize)
      throw new InvalidOperationException("No Portrait picture was read.");

    var pixels = new byte[FileSize];
    for (var i = 0; i < PlaneSize; ++i) {
      var at = i * 3;
      pixels[at] = planes[i];
      pixels[at + 1] = planes[PlaneSize + i];
      pixels[at + 2] = planes[PlaneSize * 2 + i];
    }

    return new() {
      Width = Side,
      Height = Side,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  public static PortraitFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Side || image.Height != Side)
      throw new ArgumentException($"Portrait images are fixed at {Side}x{Side} pixels.", nameof(image));

    image = image.EnsureAnyFormat(PixelFormat.Rgb24);
    if (image.PixelData.Length < FileSize)
      throw new ArgumentException("The raw image does not contain enough RGB pixel data for a Portrait picture.", nameof(image));

    var planes = new byte[FileSize];
    for (var i = 0; i < PlaneSize; ++i) {
      var at = i * 3;
      planes[i] = image.PixelData[at];
      planes[PlaneSize + i] = image.PixelData[at + 1];
      planes[PlaneSize * 2 + i] = image.PixelData[at + 2];
    }

    return new() { PlaneData = planes };
  }
}
