using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace Optimizer.Image;

/// <summary>Hand-written partial class with typed registration methods called by the source-generated <c>RegisterAll()</c>.</summary>
internal static partial class FormatRegistration {

  /// <summary>Generated at compile time by <c>FileFormat.Registry.Generator</c>.</summary>
  static partial void RegisterAll();

  internal static List<string> LoadWarnings { get; } = [];

  internal static void Initialize() {
    RegisterAll();
    _RegisterDetectionOnly();
    FormatRegistry.BuildSignatureTable();
  }

  // --- Typed registration methods (called by generated code, zero reflection) ---

  private static void _RegisterReader<T>(ImageFormat format, MagicSignature[] magic, int priority, string[] mimeTypes)
    where T : IImageFormatReader<T>, IImageToRawImage<T> {
    _ = mimeTypes; // legacy registration ignores MIME (Optimizer.Image doesn't use it)
    Func<byte[], bool?>? matchSig = null;
    try {
      // Test if the type overrides MatchesSignature (returns non-null from the static virtual default)
      matchSig = header => T.MatchesSignature(header);
    } catch {
      // Type does not override MatchesSignature — leave null
    }

    var entry = new FormatRegistry.FormatEntry(
      Format: format,
      Name: format.ToString(),
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      LoadRawImage: file => { try { return FormatIO.Decode<T>(file); } catch { return null; } },
      LoadRawImageFromBytes: bytes => { try { return FormatIO.Decode<T>(bytes); } catch { return null; } },
      ConvertFromRawImage: null,
      Capabilities: T.Capabilities,
      MagicSignatures: magic,
      MatchesSignature: matchSig,
      DetectionPriority: priority,
      VideoModes: T.VideoModes
    );

    FormatRegistry.Register(entry);
  }

  private static void _RegisterReaderWriter<T>(ImageFormat format, MagicSignature[] magic, int priority, string[] mimeTypes)
    where T : IImageFormatReader<T>, IImageToRawImage<T>, IImageFromRawImage<T>, IImageFormatWriter<T> {
    _ = mimeTypes; // legacy registration ignores MIME (Optimizer.Image doesn't use it)
    Func<byte[], bool?>? matchSig = null;
    try {
      matchSig = header => T.MatchesSignature(header);
    } catch { }

    var entry = new FormatRegistry.FormatEntry(
      Format: format,
      Name: format.ToString(),
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      LoadRawImage: file => { try { return FormatIO.Decode<T>(file); } catch { return null; } },
      LoadRawImageFromBytes: bytes => { try { return FormatIO.Decode<T>(bytes); } catch { return null; } },
      ConvertFromRawImage: raw => FormatIO.Encode<T>(raw),
      Capabilities: T.Capabilities,
      MagicSignatures: magic,
      MatchesSignature: matchSig,
      DetectionPriority: priority,
      VideoModes: T.VideoModes
    );

    FormatRegistry.Register(entry);
  }

  private static void _RegisterMultiImageReader<T>(ImageFormat format)
    where T : IImageFormatReader<T>, IImageToRawImage<T>, IMultiImageFileFormat<T> {
    FormatRegistry.AugmentMultiImage(
      format,
      file => { try { return T.ImageCount(FormatIO.Read<T>(file)); } catch { return 0; } },
      (file, index) => { try { return T.ToRawImage(FormatIO.Read<T>(file), index); } catch { return null; } },
      file => { try { return T.ToRawImages(FormatIO.Read<T>(file)); } catch { return null; } }
    );
  }

  private static void _AugmentInfoReader<T>(ImageFormat format)
    where T : IImageInfoReader<T> {
    FormatRegistry.AugmentInfoReader(
      format,
      data => { try { return T.ReadImageInfo(data); } catch { return null; } }
    );
  }

  private static void _AugmentChunkLayout<T>(ImageFormat format) where T : IFormatChunkLayout<T> {
    FormatRegistry.AugmentChunkLayout(
      format,
      data => {
        try {
          var enumerated = T.EnumerateChunks(data);
          return enumerated as IReadOnlyList<ChunkSpan> ?? new List<ChunkSpan>(enumerated);
        } catch { return new List<ChunkSpan>(); }
      });
  }

  private static void _AugmentChunkRewriter<T>(ImageFormat format) where T : IFormatChunkRewriter<T> {
    FormatRegistry.AugmentChunkRewriter(
      format,
      (data, rules) => { try { return T.Rewrite(data, rules); } catch { return data; } });
  }

  private static void _AugmentChunkPlanRewriter<T>(ImageFormat format) where T : IFormatChunkPlanRewriter<T> {
    FormatRegistry.AugmentChunkPlanRewriter(
      format,
      (data, plan) => {
        try { return T.ApplyPlan(data, plan); }
        catch (Exception ex) {
          return new ChunkRewriteResult {
            Failures = [new ChunkRewriteFailure("Validate", "(file)", 0, ex.Message)],
          };
        }
      });
  }

  private static void _RegisterDetectionOnly() {
    // GIF is now picked up automatically by the source-generated RegisterAll() via
    // FileFormat.Gif.GifFile's IImageFormatReader/Writer/MultiImage interfaces — nothing
    // hand-rolled is needed here.
  }
}
