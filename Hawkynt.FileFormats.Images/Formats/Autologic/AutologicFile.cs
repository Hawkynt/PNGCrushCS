using System;
using FileFormat.Core;

namespace FileFormat.Autologic;

/// <summary>An Autologic bitmap (.gm, .gm2, .gm4): a fourteen byte record saying how big the
/// picture is and how many levels it has, then records of line-art byte pairs.</summary>
/// <remarks>
/// This is the inline High Speed Interface form of the graphics a phototypesetter is fed, the one
/// Image Alchemy's manual says is the only one it reads. Autologic's own Input Command Language
/// manual describes the drawing side of it — a graphics header, then line-art data as byte pairs
/// with a repeat count in the second byte — but not the byte layout of the file, so the layout here
/// was taken out of XnView's own converter, whose reader for the id <c>gm</c> stands at 0x1d5f90 in
/// nconvert 7.300, and then confirmed by building files and asking that converter to read them.
/// <para/>
/// Everything is big-endian. A file opens with the record tag 0xFF04 and the word 7, which is the
/// record's length in words and therefore fourteen bytes of body: the width, the height, nine bytes
/// the reader steps over without looking at them, and one byte saying how many levels a sample has.
/// TrID's scan of twelve real files found exactly these four bytes in common and nothing beyond
/// them, which is what one would expect of a header whose remaining ten bytes all vary.
/// <para/>
/// The level byte decides both the depth and the coding. The depth is the number of bits it takes
/// to count that many levels — two levels is one bit, four is two, and so on up to eight bits above
/// 128 — and a byte of 0 or 1 leaves it at eight. A byte of 255 selects the plain form, where the
/// data records hold one raw eight bit sample a pixel; every other value selects the line-art
/// coding.
/// <para/>
/// The data follows as records, each a big-endian tag and a length in words. In the plain form the
/// tag is not looked at; in the coded form 0xFF08 is the tag a record ought to carry, and one that
/// carries anything else is still decoded but may not leave a row unfinished — the picture stops
/// there. Inside a record the coding is the byte pair the ICL manual describes, read this way: a byte
/// with the top bit clear is a sample, and a byte with the top bit set is a repeat count of
/// (byte AND 127) + 1 applying to the sample before it. So a sample on its own stands for one pixel,
/// a sample followed by a count stands for that many, and a count with no sample before it repeats
/// whatever was written last — zero at the start of the picture. A run that would overrun the row is
/// cut at the row's end; runs never wrap.
/// <para/>
/// Samples run the other way round from grey: zero is the blank medium and the top value is full
/// ink. The converter writes out (top - sample) * 255 / top, truncated, which is what
/// <see cref="ToRawImage"/> reproduces.
/// <para/>
/// What refuses a foreign file is the four bytes the file opens with, which are the tag and the
/// record length together and are as specific as a signature.
/// </remarks>
[FormatMagicBytes([0xFF, 0x04, 0x00, 0x07])]
public readonly record struct AutologicFile
  : IImageFormatReader<AutologicFile>, IImageToRawImage<AutologicFile>,
    IImageFromRawImage<AutologicFile>, IImageFormatWriter<AutologicFile> {

  /// <summary>The four bytes a file opens with: the record tag 0xFF04 and its length of seven words.</summary>
  public static ReadOnlySpan<byte> Magic => [0xFF, 0x04, 0x00, 0x07];

  /// <summary>The opening record: four bytes of tag and length, then fourteen bytes of body.</summary>
  public const int HeaderSize = 18;

  /// <summary>The tag every record of coded data has to carry.</summary>
  public const int DataRecordTag = 0xFF08;

  /// <summary>The level byte that selects raw eight bit samples instead of the line-art coding.</summary>
  public const int RawLevels = 0xFF;

  static string IImageFormatMetadata<AutologicFile>.PrimaryExtension => ".gm";
  static string[] IImageFormatMetadata<AutologicFile>.FileExtensions => [".gm", ".gm2", ".gm4"];
  static AutologicFile IImageFormatReader<AutologicFile>.FromSpan(ReadOnlySpan<byte> data) => AutologicReader.FromSpan(data);
  static byte[] IImageFormatWriter<AutologicFile>.ToBytes(AutologicFile file) => AutologicWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AutologicFile>.VideoModes => [
    new("Grey", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>The header's level byte, which picks both the depth and the coding.</summary>
  public int Levels { get; init; }

  /// <summary>One sample a pixel, one row after another, as the file carries them: nought is blank
  /// medium and <see cref="MaximumSample"/> is full ink.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>How many bits a sample takes, which is what it takes to count <see cref="Levels"/> of them.</summary>
  public int BitsPerPixel => BitsForLevels(this.Levels);

  /// <summary>The largest sample the depth can hold.</summary>
  public int MaximumSample => (1 << this.BitsPerPixel) - 1;

  /// <summary>The depth XnView derives from the level byte: enough bits to count that many levels,
  /// with 0 and 1 left at eight because the reader never asks in that case.</summary>
  public static int BitsForLevels(int levels) => levels switch {
    <= 1 => 8,
    <= 2 => 1,
    <= 4 => 2,
    <= 8 => 3,
    <= 16 => 4,
    <= 32 => 5,
    <= 64 => 6,
    <= 128 => 7,
    _ => 8,
  };

  public static RawImage ToRawImage(AutologicFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    var top = file.MaximumSample;
    var grey = new byte[file.PixelData.Length];
    for (var i = 0; i < grey.Length; ++i) {
      // A sample larger than the depth allows should not occur, and the converter does not treat
      // one uniformly: at one bit it keeps the bottom bit and at every other depth it saturates.
      var sample = top == 1 ? file.PixelData[i] & 1 : Math.Min((int)file.PixelData[i], top);
      grey[i] = (byte)((top - sample) * 255 / top);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = grey,
    };
  }

  /// <summary>Builds the plain eight bit form, the one that can carry every grey without loss.</summary>
  public static AutologicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var source = image.EnsureFormat(PixelFormat.Gray8);
    var samples = new byte[source.PixelData.Length];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (byte)(255 - source.PixelData[i]);

    return new() {
      Width = source.Width,
      Height = source.Height,
      Levels = RawLevels,
      PixelData = samples,
    };
  }
}
