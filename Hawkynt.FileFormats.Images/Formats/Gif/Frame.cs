using System;

namespace FileFormat.Gif;

/// <summary>One GIF frame — image descriptor + optional local colour table + decoded pixel indices,
/// plus the GCE state that applied to it.</summary>
/// <remarks>
/// <para>Pixel data is byte-per-pixel indices in row-major top-down order, regardless of whether the
/// original was interlaced — the reader de-interlaces.</para>
/// <para>If <see cref="LocalColorTable"/> is null, the frame uses the parent file's
/// <see cref="GifFile.GlobalColorTable"/>.</para>
/// </remarks>
public sealed class Frame {

  /// <summary>Image-descriptor X offset on the logical screen.</summary>
  public required ushort Left { get; init; }
  /// <summary>Image-descriptor Y offset on the logical screen.</summary>
  public required ushort Top { get; init; }
  /// <summary>Frame width in pixels.</summary>
  public required ushort Width { get; init; }
  /// <summary>Frame height in pixels.</summary>
  public required ushort Height { get; init; }

  /// <summary>Local colour table — packed RGB triplets, <c>3 * 2^(LocalColorTableSize+1)</c> bytes.
  /// <c>null</c> when the frame falls back to the global colour table.</summary>
  public byte[]? LocalColorTable { get; init; }

  /// <summary>True when the local colour table is sorted by frequency.</summary>
  public bool LocalColorTableSorted { get; init; }

  /// <summary>Number of entries in the local colour table, or 0 when there isn't one.</summary>
  public int LocalColorTableEntryCount => this.LocalColorTable?.Length / 3 ?? 0;

  /// <summary>True if the original on-disk frame stored pixels interlaced (4-pass GIF interlacing).
  /// Pixels in <see cref="PixelData"/> are always linear (the reader de-interlaces); this flag is preserved
  /// so the writer can re-emit interlaced if requested.</summary>
  public bool IsInterlaced { get; init; }

  /// <summary>Decoded indexed pixel data, byte-per-pixel, top-down row-major, length = <c>Width * Height</c>.</summary>
  public required byte[] PixelData { get; init; } = Array.Empty<byte>();

  /// <summary>Frame display delay (GIF89a only). Stored as hundredths of a second on disk; converted to <see cref="TimeSpan"/>
  /// here for convenience. <see cref="TimeSpan.Zero"/> means "no GCE delay was present".</summary>
  public TimeSpan Delay { get; init; }

  /// <summary>Disposal method from the Graphic Control Extension (GIF89a).</summary>
  public FrameDisposalMethod DisposalMethod { get; init; }

  /// <summary>True when the frame's GCE requested user input before advancing (rarely used).</summary>
  public bool UserInputFlag { get; init; }

  /// <summary>The transparent colour index from the GCE, or <c>null</c> when the transparency flag was unset.</summary>
  public byte? TransparentColorIndex { get; init; }

  // ---- compat accessors matching the external Hawkynt.GifFileFormat.Frame API ----

  /// <summary>Alias for <see cref="PixelData"/> matching the external API.</summary>
  public byte[] IndexedPixels => this.PixelData;

  /// <summary>(<see cref="Width"/>, <see cref="Height"/>) packaged as a <see cref="Dimensions"/>.</summary>
  public Dimensions Size => new(this.Width, this.Height);

  /// <summary>(<see cref="Left"/>, <see cref="Top"/>) packaged as an <see cref="Offset"/>.</summary>
  public Offset Position => new(this.Left, this.Top);

  /// <summary>Default constructor.</summary>
  public Frame() { }

  /// <summary>Positional constructor matching the external API, for migration ease.</summary>
  [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
  public Frame(
    byte[] indexedPixels,
    Dimensions size,
    Offset position,
    byte[]? localColorTable,
    TimeSpan delay,
    FrameDisposalMethod disposalMethod,
    byte? transparentColorIndex,
    bool isInterlaced = false) {
    this.PixelData = indexedPixels;
    this.Width = size.Width;
    this.Height = size.Height;
    this.Left = position.X;
    this.Top = position.Y;
    this.LocalColorTable = localColorTable;
    this.Delay = delay;
    this.DisposalMethod = disposalMethod;
    this.TransparentColorIndex = transparentColorIndex;
    this.IsInterlaced = isInterlaced;
  }

  /// <summary>Returns a clone of this frame with a different <see cref="Delay"/>.</summary>
  public Frame WithDelay(TimeSpan newDelay) => new() {
    Left = this.Left, Top = this.Top, Width = this.Width, Height = this.Height,
    LocalColorTable = this.LocalColorTable,
    LocalColorTableSorted = this.LocalColorTableSorted,
    IsInterlaced = this.IsInterlaced,
    PixelData = this.PixelData,
    Delay = newDelay,
    DisposalMethod = this.DisposalMethod,
    UserInputFlag = this.UserInputFlag,
    TransparentColorIndex = this.TransparentColorIndex,
  };
}
