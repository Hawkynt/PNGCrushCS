using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.PlaybackBitmapSequence;

/// <summary>A playback bitmap sequence (.bms): ten letters, six bytes, and a Windows bitmap.</summary>
/// <remarks>
/// XnView calls the format Playback Bitmap Sequence. Nothing about it has been published and no
/// sample of it could be found; what identifies it is the ten letters it opens with,
/// <c>BMSWinPlay</c>, which name the program that wrote it. Behind them stand six bytes this does not
/// read and then a complete Windows bitmap, whose own offsets are counted from where it starts.
/// <para/>
/// That was established against XnView's own converter, which reads the format: a file built as ten
/// letters, six zeros and an ordinary bitmap is reported by it as a Playback Bitmap Sequence of the
/// right size, and what it converts out is byte for byte what it converts out of the same bitmap
/// standing alone. Changing one letter of the ten makes it refuse the file.
/// <para/>
/// The name says sequence and a file may well hold more than one. The one built here held one and
/// XnView reported one page; where the second would stand, and whether there is a count anywhere in
/// the six bytes, is not known, so one picture is read — the one the format's own header leads to.
/// </remarks>
[FormatMagicBytes([0x42, 0x4D, 0x53, 0x57, 0x69, 0x6E, 0x50, 0x6C, 0x61, 0x79])]
public readonly record struct PlaybackBitmapSequenceFile
  : IImageFormatReader<PlaybackBitmapSequenceFile>, IImageToRawImage<PlaybackBitmapSequenceFile>, IImageFromRawImage<PlaybackBitmapSequenceFile>, IImageFormatWriter<PlaybackBitmapSequenceFile> {

  /// <summary>The ten letters a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "BMSWinPlay"u8;

  /// <summary>The letters and the six bytes behind them.</summary>
  public const int HeaderSize = 16;

  static string IImageFormatMetadata<PlaybackBitmapSequenceFile>.PrimaryExtension => ".bms";
  static string[] IImageFormatMetadata<PlaybackBitmapSequenceFile>.FileExtensions => [".bms"];
  static PlaybackBitmapSequenceFile IImageFormatReader<PlaybackBitmapSequenceFile>.FromSpan(ReadOnlySpan<byte> data) => PlaybackBitmapSequenceReader.FromSpan(data);
  static byte[] IImageFormatWriter<PlaybackBitmapSequenceFile>.ToBytes(PlaybackBitmapSequenceFile file) => PlaybackBitmapSequenceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PlaybackBitmapSequenceFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  /// <summary>The bitmap the file carries, whole.</summary>
  public byte[] Bitmap { get; init; }

  public static RawImage ToRawImage(PlaybackBitmapSequenceFile file) {
    if (file.Bitmap == null)
      throw new InvalidOperationException("No picture was read.");

    return BmpFile.ToRawImage(BmpReader.FromSpan(file.Bitmap));
  }

  /// <summary>Creates the externally-verified single-picture form by embedding an ordinary BMP.</summary>
  public static PlaybackBitmapSequenceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Bitmap = BmpWriter.ToBytes(BmpFile.FromRawImage(image)) };
  }
}
