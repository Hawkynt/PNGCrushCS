using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Core;
using FileFormat.Sgi;

namespace Optimizer.Sgi;

/// <summary>
/// Writes an SGI image every way the format allows and keeps the smallest, without altering a pixel.
/// </summary>
/// <remarks>
/// <para>
/// SGI leaves four things open that cost bytes and change nothing anybody can see: whether scanlines
/// are run-length encoded, how many channels are stored, how wide a sample is, and whether the
/// 80-byte name field is filled in. A writer picks one answer for all of them and moves on; this
/// tries each and measures.
/// </para>
/// <para>
/// The channel and depth reductions are only offered where they are provably reversible — three
/// channels collapse to one only if the image is grey in every pixel, an alpha channel is dropped
/// only if it is opaque in every pixel, and 16-bit samples narrow to 8 only if each one's two bytes
/// are equal. Where that does not hold the combination is not generated at all, so no run can
/// produce an image that differs from the one it was given.
/// </para>
/// </remarks>
public sealed class SgiOptimizer {

  private readonly RawImage _image;
  private readonly SgiOptimizationOptions _options;

  public SgiOptimizer(RawImage image, SgiOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    this._image = image;
    this._options = options ?? new SgiOptimizationOptions();
  }

  public static SgiOptimizer FromFile(FileInfo file, SgiOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SGI file not found.", file.FullName);

    var sgi = SgiReader.FromFile(file);
    return new(SgiFile.ToRawImage(sgi), options);
  }

  /// <summary>Encodes every allowed combination and returns the smallest.</summary>
  public async ValueTask<SgiOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default) {
    var combos = this._GenerateCombinations();
    if (combos.Length == 0)
      throw new InvalidOperationException("No encoding combination was applicable to this image.");

    var results = new List<SgiOptimizationResult>();
    var gate = new object();
    using var semaphore = new SemaphoreSlim(this._options.MaxParallelTasks);

    await Task.WhenAll(combos.Select(async combo => {
      await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      try {
        var started = Stopwatch.GetTimestamp();
        var bytes = this._Encode(combo);
        var result = new SgiOptimizationResult(combo, bytes.LongLength, Stopwatch.GetElapsedTime(started), bytes);
        lock (gate)
          results.Add(result);
      } finally {
        semaphore.Release();
      }
    })).ConfigureAwait(false);

    // Ties go to the simpler encoding, so a run that saves nothing does not add compression for it.
    return results
      .OrderBy(r => r.CompressedSize)
      .ThenBy(r => r.Combo.Compression)
      .First();
  }

  /// <summary>The combinations that are safe for this particular image.</summary>
  private SgiOptimizationCombo[] _GenerateCombinations() {
    var source = this._image;
    var channels = _ChannelsOf(source.Format);
    var bytesPerChannel = _BytesPerChannelOf(source.Format);

    var channelChoices = new List<int> { channels };
    if (this._options.ReduceChannels) {
      if (channels >= 3 && _IsGreyEverywhere(source, channels, bytesPerChannel))
        channelChoices.Add(1);
      if (channels == 4 && _IsOpaqueEverywhere(source, bytesPerChannel))
        channelChoices.Add(3);
    }

    var depthChoices = new List<int> { bytesPerChannel };
    if (this._options.ReduceDepth && bytesPerChannel == 2 && _FitsInEightBits(source))
      depthChoices.Add(1);

    var nameChoices = this._options.DropImageName ? new[] { false, true } : [true];

    return (from compression in this._options.Compressions
            from channelCount in channelChoices.Distinct()
            from depth in depthChoices.Distinct()
            from keepName in nameChoices
            select new SgiOptimizationCombo(compression, channelCount, depth, keepName)).ToArray();
  }

  private byte[] _Encode(SgiOptimizationCombo combo) {
    var reduced = _Reduce(this._image, combo.Channels, combo.BytesPerChannel);
    var file = SgiFile.FromRawImage(reduced) with {
      Compression = combo.Compression,
      ImageName = combo.KeepImageName ? "optimized" : string.Empty,
    };

    return SgiWriter.ToBytes(file);
  }

  // ---- what is safe to drop ----

  /// <summary>Whether every pixel's colour channels are equal, which makes the image a grey one.</summary>
  private static bool _IsGreyEverywhere(RawImage image, int channels, int bytesPerChannel) {
    var pixels = image.PixelData;
    var stride = channels * bytesPerChannel;
    for (var at = 0; at + stride <= pixels.Length; at += stride) {
      for (var c = 1; c < 3; ++c)
        for (var b = 0; b < bytesPerChannel; ++b)
          if (pixels[at + b] != pixels[at + (c * bytesPerChannel) + b])
            return false;
    }

    return true;
  }

  /// <summary>Whether the alpha channel is fully opaque everywhere, and so says nothing.</summary>
  private static bool _IsOpaqueEverywhere(RawImage image, int bytesPerChannel) {
    var pixels = image.PixelData;
    var stride = 4 * bytesPerChannel;
    for (var at = 0; at + stride <= pixels.Length; at += stride)
      for (var b = 0; b < bytesPerChannel; ++b)
        if (pixels[at + (3 * bytesPerChannel) + b] != 0xFF)
          return false;

    return true;
  }

  /// <summary>Whether every 16-bit sample repeats its high byte, and so loses nothing at 8 bits.</summary>
  private static bool _FitsInEightBits(RawImage image) {
    var pixels = image.PixelData;
    for (var at = 0; at + 1 < pixels.Length; at += 2)
      if (pixels[at] != pixels[at + 1])
        return false;

    return true;
  }

  // ---- reductions ----

  private static RawImage _Reduce(RawImage image, int channels, int bytesPerChannel) {
    var sourceChannels = _ChannelsOf(image.Format);
    var sourceDepth = _BytesPerChannelOf(image.Format);
    if (channels == sourceChannels && bytesPerChannel == sourceDepth)
      return image;

    var pixelCount = image.Width * image.Height;
    var result = new byte[pixelCount * channels * bytesPerChannel];

    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < channels; ++c)
        for (var b = 0; b < bytesPerChannel; ++b) {
          // A narrowed sample keeps its high byte, which is the whole value when the two are equal.
          var sourceByte = sourceDepth == bytesPerChannel ? b : 0;
          var from = ((i * sourceChannels) + c) * sourceDepth + sourceByte;
          result[(((i * channels) + c) * bytesPerChannel) + b] = from < image.PixelData.Length ? image.PixelData[from] : (byte)0;
        }

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = _FormatFor(channels, bytesPerChannel),
      PixelData = result,
    };
  }

  private static PixelFormat _FormatFor(int channels, int bytesPerChannel) => (channels, bytesPerChannel) switch {
    (1, 1) => PixelFormat.Gray8,
    (1, 2) => PixelFormat.Gray16,
    (3, 1) => PixelFormat.Rgb24,
    (3, 2) => PixelFormat.Rgb48,
    (4, 1) => PixelFormat.Rgba32,
    (4, 2) => PixelFormat.Rgba64,
    _ => throw new NotSupportedException($"SGI cannot store {channels} channels at {bytesPerChannel} byte(s) each."),
  };

  private static int _ChannelsOf(PixelFormat format) => format switch {
    PixelFormat.Gray8 or PixelFormat.Gray16 => 1,
    PixelFormat.Rgb24 or PixelFormat.Rgb48 => 3,
    PixelFormat.Rgba32 or PixelFormat.Rgba64 => 4,
    _ => throw new NotSupportedException($"SGI cannot store {format}."),
  };

  private static int _BytesPerChannelOf(PixelFormat format) => format switch {
    PixelFormat.Gray16 or PixelFormat.Rgb48 or PixelFormat.Rgba64 => 2,
    _ => 1,
  };
}
