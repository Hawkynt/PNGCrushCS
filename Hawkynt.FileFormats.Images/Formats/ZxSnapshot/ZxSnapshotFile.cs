using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZxSpectrum;

namespace FileFormat.ZxSnapshot;

/// <summary>In-memory representation of a ZX Spectrum memory snapshot (.sna, .z80).</summary>
/// <remarks>
/// Not a picture format at all: a snapshot is the machine's registers followed by its whole memory,
/// saved so that a session can be resumed. But the screen is part of that memory and always at the
/// same place — the first 6912 bytes of it — so a snapshot shows exactly what the display showed
/// when it was taken, which is why every viewer that knows the machine opens one.
/// <para/>
/// The 48K snapshot is 27 bytes of registers and 49152 of memory. The 128K one appends its extra
/// banks; the screen is in the same place either way, because the bank the display reads from is
/// the one the file stores first.
/// </remarks>
public readonly record struct ZxSnapshotFile
  : IImageFormatReader<ZxSnapshotFile>, IImageToRawImage<ZxSnapshotFile>,
    IImageFromRawImage<ZxSnapshotFile>, IImageFormatWriter<ZxSnapshotFile> {

  /// <summary>Bytes of register state before the memory image.</summary>
  public const int HeaderSize = 27;

  /// <summary>Where the border colour sits in that state.</summary>
  public const int BorderOffset = 26;

  /// <summary>Bytes the screen occupies at the start of memory.</summary>
  public const int ScreenSize = 6912;

  /// <summary>The length of a 48K snapshot.</summary>
  public const int ShortFileSize = HeaderSize + 49152;

  /// <summary>The lengths a 128K snapshot comes in, its extra banks following the first 48K.</summary>
  public const int LongFileSize = ShortFileSize + 4 + 16384 * 5;

  public const int LongerFileSize = ShortFileSize + 4 + 16384 * 6;

  static string IImageFormatMetadata<ZxSnapshotFile>.PrimaryExtension => ".sna";
  static string[] IImageFormatMetadata<ZxSnapshotFile>.FileExtensions => [".sna"];
  static ZxSnapshotFile IImageFormatReader<ZxSnapshotFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxSnapshotReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxSnapshotFile>.ToBytes(ZxSnapshotFile file) => ZxSnapshotWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxSnapshotFile>.VideoModes => [
    new("ZX Spectrum", [(256, 192)], [15])
  ];

  /// <summary>The screen as it stood, bitmap then attributes.</summary>
  public byte[] Screen { get; init; }

  /// <summary>The border colour, which the registers hold rather than the screen.</summary>
  public byte BorderColor { get; init; }

  public int Width => 256;

  public int Height => 192;

  /// <summary>Hands the screen to the reader that already knows how to draw one.</summary>
  public static RawImage ToRawImage(ZxSnapshotFile file)
    => ZxSpectrumFile.ToRawImage(ZxSpectrumReader.FromSpan(file.Screen ?? new byte[ScreenSize]));

  /// <summary>Builds a snapshot whose memory holds the given picture where the display reads it.</summary>
  /// <remarks>
  /// This writes a machine that is not running anything: the registers are left at nought and the
  /// rest of memory empty. What comes out is not a resumable session and does not pretend to be one
  /// — it is a snapshot whose screen is this picture, which is the whole of what the format holds
  /// that anything here can supply.
  /// </remarks>
  public static ZxSnapshotFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The screen is a Spectrum screen, so the encoder that already makes one makes this one.
    return new() {
      Screen = ZxSpectrumWriter.ToBytes(ZxSpectrumFile.FromRawImage(image)),
      BorderColor = 0,
    };
  }
}
