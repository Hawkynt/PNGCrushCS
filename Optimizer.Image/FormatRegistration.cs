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

  private static void _RegisterReader<T>(ImageFormat format, FormatRegistry.MagicSignature[] magic, int priority)
    where T : IImageFormatReader<T>, IImageToRawImage<T> {
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
      DetectionPriority: priority
    );

    FormatRegistry.Register(entry);
  }

  private static void _RegisterReaderWriter<T>(ImageFormat format, FormatRegistry.MagicSignature[] magic, int priority)
    where T : IImageFormatReader<T>, IImageToRawImage<T>, IImageFromRawImage<T>, IImageFormatWriter<T> {
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
      DetectionPriority: priority
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

  private static void _RegisterDetectionOnly() {
    // GIF uses external GifFileFormat library with its own type system (not IImageFormatReader)
    var gifEntry = new FormatRegistry.FormatEntry(
      Format: ImageFormat.Gif,
      Name: "Gif",
      PrimaryExtension: ".gif",
      AllExtensions: [".gif", ".giff"],
      LoadRawImage: file => { try { return _LoadGifFrame(file, 0); } catch { return null; } },
      LoadRawImageFromBytes: bytes => null,
      ConvertFromRawImage: null,
      Capabilities: FormatCapability.VariableResolution | FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage,
      MagicSignatures: [new([0x47, 0x49, 0x46, 0x38], 0, 4)],
      MatchesSignature: null,
      DetectionPriority: 100,
      GetImageCount: file => { try { return Hawkynt.GifFileFormat.Reader.FromFile(file).Frames.Count; } catch { return 0; } },
      LoadRawImageAtIndex: (file, index) => { try { return _LoadGifFrame(file, index); } catch { return null; } },
      LoadAllRawImages: null
    );
    FormatRegistry.Register(gifEntry);
  }

  private static RawImage? _LoadGifFrame(FileInfo file, int index) {
    var gif = Hawkynt.GifFileFormat.Reader.FromFile(file);
    if (index < 0 || index >= gif.Frames.Count) return null;

    var frame = gif.Frames[index];
    var palette = frame.LocalColorTable ?? gif.GlobalColorTable;
    if (palette == null) return null;

    var width = frame.Size.Width;
    var height = frame.Size.Height;
    var indices = frame.IndexedPixels;
    var rgba = new byte[width * height * 4];

    for (var i = 0; i < indices.Length && i < width * height; ++i) {
      var color = palette[indices[i]];
      var j = i * 4;
      rgba[j] = color.R;
      rgba[j + 1] = color.G;
      rgba[j + 2] = color.B;
      rgba[j + 3] = indices[i] == frame.TransparentColorIndex ? (byte)0 : color.A;
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba
    };
  }
}
