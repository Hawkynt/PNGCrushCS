using System;
using FileFormat.Core;

namespace FileFormat.GraspGl;

/// <summary>
/// GRASP GL (Microtex GRASP) animation file: 2-byte little-endian directory offset, followed by
/// up to N entries of (13-byte zero-padded filename + 16-bit reserved). Each entry typically points
/// to a CLP, PIC, or SET sub-file stored later in the stream. This implementation models the
/// container only — embedded sub-images are exposed as raw byte slices via <see cref="Entries"/>.
/// </summary>
public readonly record struct GraspGlFile : IImageFormatReader<GraspGlFile>, IImageFormatWriter<GraspGlFile>, IImageToRawImage<GraspGlFile>, IImageFromRawImage<GraspGlFile> {

  static string IImageFormatMetadata<GraspGlFile>.PrimaryExtension => ".gl";
  static string[] IImageFormatMetadata<GraspGlFile>.FileExtensions => [".gl"];
  static GraspGlFile IImageFormatReader<GraspGlFile>.FromSpan(ReadOnlySpan<byte> data) => GraspGlReader.FromSpan(data);
  static byte[] IImageFormatWriter<GraspGlFile>.ToBytes(GraspGlFile file) => GraspGlWriter.ToBytes(file);

  public sealed record GraspEntry(string Name, byte[] Data);

  public GraspEntry[] Entries { get; init; }

  public static RawImage ToRawImage(GraspGlFile file) {
    // GRASP holds zero or more embedded images; we expose only the first entry's geometry guess
    // (no codec dispatch — interpreting an embedded PIC/CLP belongs to its own format library).
    ArgumentNullException.ThrowIfNull(file.Entries);
    if (file.Entries.Length == 0)
      throw new InvalidOperationException("GRASP GL container has no entries.");

    // Fallback: present the first entry's raw bytes as a 1xN grayscale stripe so the
    // platform-independent surface is non-empty. Real applications would route to PCX/CLP/SET decoders.
    var payload = file.Entries[0].Data;
    return new() {
      Width = payload.Length,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = payload,
      Palette = null,
      PaletteCount = 0,
    };
  }

  public static GraspGlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    // Round-trip the platform-independent surface as a single anonymous entry.
    return new() {
      Entries = [new GraspEntry("FRAME.RAW", (byte[])image.PixelData.Clone())],
    };
  }
}
