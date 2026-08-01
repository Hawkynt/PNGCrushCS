using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images;

/// <summary>
/// Hand-written partial of the source-generated <c>FormatRegistration.g.cs</c>. Hosts the
/// typed registration methods (<c>_RegisterReader&lt;T&gt;</c> etc.) that the generated
/// <c>RegisterAll()</c> calls — these use static-interface dispatch to invoke each format's
/// <see cref="IImageFormatReader{TSelf}.FromSpan"/> / <see cref="IImageFormatWriter{TSelf}.ToBytes"/>
/// without any runtime reflection.
/// </summary>
internal static partial class FormatRegistration {

  /// <summary>Implemented by the source generator (<c>FormatRegistration.g.cs</c>).</summary>
  static partial void RegisterAll();

  internal static void Initialize() {
    RegisterAll();
    FormatRegistry.BuildSignatureTable();
  }

  // -------- Typed registration entry points (called only by generated code) --------

  private static void _RegisterReader<T>(ImageFormat format, MagicSignature[] magic, int priority, string[] mimeTypes)
    where T : IImageFormatReader<T>, IImageToRawImage<T> {
    Func<byte[], bool?>? matchSig = null;
    try { matchSig = header => T.MatchesSignature(header); } catch { /* type doesn't override */ }

    var entry = new FormatEntry(
      Format: format,
      Name: format.ToString(),
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      MimeTypes: mimeTypes,
      Capabilities: T.Capabilities,
      MagicSignatures: magic,
      MatchesSignature: matchSig,
      DetectionPriority: priority,
      LoadRawImage: file => { try { return FormatIO.Decode<T>(file); } catch { return null; } },
      LoadRawImageFromBytes: bytes => { try { return FormatIO.Decode<T>(bytes); } catch { return null; } },
      ConvertFromRawImage: null,
      VideoModes: T.VideoModes,
      LoadRawImageOrThrow: FormatIO.Decode<T>);
    FormatRegistry.Register(entry);
  }

  private static void _RegisterReaderWriter<T>(ImageFormat format, MagicSignature[] magic, int priority, string[] mimeTypes)
    where T : IImageFormatReader<T>, IImageToRawImage<T>, IImageFromRawImage<T>, IImageFormatWriter<T> {
    Func<byte[], bool?>? matchSig = null;
    try { matchSig = header => T.MatchesSignature(header); } catch { /* type doesn't override */ }

    var entry = new FormatEntry(
      Format: format,
      Name: format.ToString(),
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      MimeTypes: mimeTypes,
      Capabilities: T.Capabilities,
      MagicSignatures: magic,
      MatchesSignature: matchSig,
      DetectionPriority: priority,
      LoadRawImage: file => { try { return FormatIO.Decode<T>(file); } catch { return null; } },
      LoadRawImageFromBytes: bytes => { try { return FormatIO.Decode<T>(bytes); } catch { return null; } },
      ConvertFromRawImage: raw => FormatIO.Encode<T>(raw),
      VideoModes: T.VideoModes,
      LoadRawImageOrThrow: FormatIO.Decode<T>);
    FormatRegistry.Register(entry);
  }

  private static void _RegisterMultiImageReader<T>(ImageFormat format)
    where T : IImageFormatReader<T>, IImageToRawImage<T>, IMultiImageFileFormat<T> {
    FormatRegistry.AugmentMultiImage(
      format,
      file => { try { return T.ImageCount(FormatIO.Read<T>(file)); } catch { return 0; } },
      (file, index) => { try { return T.ToRawImage(FormatIO.Read<T>(file), index); } catch { return null; } },
      file => { try { return T.ToRawImages(FormatIO.Read<T>(file)); } catch { return null; } });
  }

  private static void _AugmentInfoReader<T>(ImageFormat format) where T : IImageInfoReader<T> {
    FormatRegistry.AugmentInfoReader(
      format,
      data => { try { return T.ReadImageInfo(data); } catch { return null; } });
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
}
