using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP;

/// <summary>Controls advanced VP8 lossy encoding.</summary>
public sealed record WebPLossyEncodingOptions {
  /// <summary>Initial VP8 quality in the inclusive range 0..100.</summary>
  public int Quality { get; init; } = 75;

  /// <summary>Minimum quality the rate-control search may select.</summary>
  public int MinQuality { get; init; }

  /// <summary>Maximum quality the rate-control search may select.</summary>
  public int MaxQuality { get; init; } = 100;

  /// <summary>Maximum number of rate-control passes. Valid values are 1..10.</summary>
  public int Passes { get; init; } = 6;

  /// <summary>
  /// Optional target size of the complete WebP file in bytes. This and <see cref="TargetPsnr"/>
  /// are mutually exclusive.
  /// </summary>
  public int? TargetSizeBytes { get; init; }

  /// <summary>
  /// Optional target RGB PSNR in dB. This and <see cref="TargetSizeBytes"/> are mutually exclusive.
  /// Alpha is excluded because lossy WebP stores it losslessly in the separate ALPH chunk.
  /// </summary>
  public double? TargetPsnr { get; init; }

  /// <summary>Number of VP8 coefficient token partitions. VP8 permits exactly 1, 2, 4, or 8.</summary>
  public int TokenPartitions { get; init; } = 1;

  /// <summary>
  /// Emits independent coefficient partitions concurrently when more than one token partition is
  /// requested. Output is deterministic and byte-identical to serial partition emission.
  /// </summary>
  public bool UseTokenPartitionThreading { get; init; } = true;
}

/// <summary>Diagnostic result returned by <see cref="WebPLossyEncoder.EncodeWithResult"/>.</summary>
public sealed record WebPLossyEncodingResult(
  WebPFile File,
  int Quality,
  int EncodedSizeBytes,
  double? Psnr,
  int PassesUsed);

/// <summary>
/// Advanced VP8/WebP lossy encoder with token-partition threading and multi-pass rate control.
/// </summary>
public static class WebPLossyEncoder {

  private const double _QUALITY_CONVERGENCE_LIMIT = 0.4;
  private const double _MAX_QUALITY_STEP = 30.0;
  private const double _INITIAL_QUALITY_STEP = 10.0;

  private sealed record Candidate(WebPFile File, int Quality, int Size, double? Psnr);

  /// <summary>Encodes a lossy WebP using <paramref name="options"/>.</summary>
  public static WebPFile Encode(RawImage image, WebPLossyEncodingOptions? options = null)
    => EncodeWithResult(image, options).File;

  /// <summary>
  /// Encodes a lossy WebP and reports the selected quality, final encoded size, measured PSNR when
  /// requested, and the number of attempted rate-control passes.
  /// </summary>
  public static WebPLossyEncodingResult EncodeWithResult(RawImage image, WebPLossyEncodingOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    options ??= new();
    _Validate(options);

    var metadata = WebPMetadataCodec.Write(image.Metadata);
    var alpha = _GetUsefulAlpha(image);
    byte[]? referenceRgb = null;
    if (options.TargetPsnr is not null)
      referenceRgb = image.Format == PixelFormat.Rgb24
        ? image.PixelData
        : PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

    var minQuality = options.MinQuality;
    var maxQuality = options.MaxQuality;
    var quality = Math.Clamp(options.Quality, minQuality, maxQuality);
    var target = options.TargetSizeBytes is int targetSize ? targetSize : options.TargetPsnr;

    // With no metric target there is nothing for additional passes to converge on. Still use the
    // advanced partition path so token partitioning/threading remains available independently.
    if (target is null) {
      var candidate = _EncodeCandidate(image, metadata, alpha, referenceRgb, quality, options);
      return new(candidate.File, candidate.Quality, candidate.Size, candidate.Psnr, 1);
    }

    var attempted = new HashSet<int>();
    Candidate? best = null;
    var passesUsed = 0;

    var havePrevious = false;
    var previousQuality = quality;
    var previousValue = 0.0;

    // The metrics used here (encoded size and PSNR) are both normally increasing functions of
    // quality. The bracket is only a guard rail for pathological/non-monotonic local points; the
    // secant step below remains the primary libwebp-style convergence mechanism.
    var lowerQuality = minQuality;
    var upperQuality = maxQuality;

    for (var pass = 0; pass < options.Passes; ++pass) {
      if (!attempted.Add(quality))
        break;

      var candidate = _EncodeCandidate(image, metadata, alpha, referenceRgb, quality, options);
      ++passesUsed;
      best = _ChooseBetter(best, candidate, options);

      var value = options.TargetSizeBytes is not null ? candidate.Size : candidate.Psnr!.Value;
      var targetValue = target.Value;
      if (value < targetValue)
        lowerQuality = Math.Max(lowerQuality, quality);
      else if (value > targetValue)
        upperQuality = Math.Min(upperQuality, quality);
      else
        break;

      if (pass + 1 >= options.Passes)
        break;

      double delta;
      if (!havePrevious) {
        delta = value > targetValue ? -_INITIAL_QUALITY_STEP : _INITIAL_QUALITY_STEP;
      } else if (Math.Abs(previousValue - value) > double.Epsilon) {
        delta = (targetValue - value) / (previousValue - value) * (previousQuality - quality);
        delta = Math.Clamp(delta, -_MAX_QUALITY_STEP, _MAX_QUALITY_STEP);
      } else {
        delta = value > targetValue ? -1.0 : 1.0;
      }

      previousQuality = quality;
      previousValue = value;
      havePrevious = true;

      if (Math.Abs(delta) <= _QUALITY_CONVERGENCE_LIMIT)
        break;

      var nextQuality = (int)Math.Round(quality + delta, MidpointRounding.AwayFromZero);
      nextQuality = Math.Clamp(nextQuality, minQuality, maxQuality);

      // Once samples exist on both sides of the target, keep the next point inside the known
      // bracket. This prevents secant overshoot while retaining its fast convergence.
      if (lowerQuality < upperQuality)
        nextQuality = Math.Clamp(nextQuality, lowerQuality, upperQuality);

      if (nextQuality == quality || attempted.Contains(nextQuality))
        nextQuality = _PickUntriedQuality(quality, value < targetValue, lowerQuality, upperQuality, attempted);
      if (nextQuality < 0)
        break;

      quality = nextQuality;
    }

    var selected = best ?? throw new InvalidOperationException("Rate control produced no VP8 candidate.");
    return new(selected.File, selected.Quality, selected.Size, selected.Psnr, passesUsed);
  }

  private static Candidate _EncodeCandidate(
    RawImage image,
    List<(string ChunkId, byte[] Data)> metadata,
    byte[]? alpha,
    byte[]? referenceRgb,
    int quality,
    WebPLossyEncodingOptions options) {

    var vp8 = Vp8Encoder.Encode(image, quality, options.TokenPartitions, options.UseTokenPartitionThreading);
    var hasAlpha = alpha is not null;
    var file = new WebPFile {
      Features = new(image.Width, image.Height, hasAlpha, IsLossless: false, IsAnimated: false),
      MetadataChunks = metadata,
      ImageData = vp8,
      IsLossless = false,
      AlphaData = alpha,
    };

    var size = WebPWriter.ToBytes(file).Length;
    double? psnr = null;
    if (referenceRgb is not null) {
      var decoded = Vp8Decoder.Decode(vp8, image.Width, image.Height);
      psnr = _ComputePsnr(referenceRgb, decoded);
    }

    return new(file, quality, size, psnr);
  }

  private static Candidate _ChooseBetter(Candidate? current, Candidate candidate, WebPLossyEncodingOptions options) {
    if (current is null)
      return candidate;

    if (options.TargetSizeBytes is int targetSize) {
      var currentError = Math.Abs((long)current.Size - targetSize);
      var candidateError = Math.Abs((long)candidate.Size - targetSize);
      if (candidateError != currentError)
        return candidateError < currentError ? candidate : current;

      // Equal error: prefer not exceeding the budget, then the smaller result.
      var currentFits = current.Size <= targetSize;
      var candidateFits = candidate.Size <= targetSize;
      if (candidateFits != currentFits)
        return candidateFits ? candidate : current;
      return candidate.Size < current.Size ? candidate : current;
    }

    var targetPsnr = options.TargetPsnr!.Value;
    var currentPsnr = current.Psnr!.Value;
    var candidatePsnr = candidate.Psnr!.Value;
    var currentMeets = currentPsnr >= targetPsnr;
    var candidateMeets = candidatePsnr >= targetPsnr;

    if (candidateMeets != currentMeets)
      return candidateMeets ? candidate : current;
    if (candidateMeets) {
      if (candidate.Size != current.Size)
        return candidate.Size < current.Size ? candidate : current;
      return candidate.Quality < current.Quality ? candidate : current;
    }

    if (Math.Abs(candidatePsnr - currentPsnr) > 1e-12)
      return candidatePsnr > currentPsnr ? candidate : current;
    return candidate.Size < current.Size ? candidate : current;
  }

  private static int _PickUntriedQuality(
    int currentQuality,
    bool needHigherMetric,
    int lowerQuality,
    int upperQuality,
    HashSet<int> attempted) {

    if (lowerQuality < upperQuality) {
      var midpoint = lowerQuality + (upperQuality - lowerQuality) / 2;
      if (!attempted.Contains(midpoint) && midpoint != currentQuality)
        return midpoint;
    }

    var direction = needHigherMetric ? 1 : -1;
    for (var distance = 1; distance <= 100; ++distance) {
      var q = currentQuality + direction * distance;
      if (q < lowerQuality || q > upperQuality)
        break;
      if (!attempted.Contains(q))
        return q;
    }

    return -1;
  }

  private static byte[]? _GetUsefulAlpha(RawImage image) {
    if (image.Format != PixelFormat.Rgba32)
      return null;

    var pixelCount = checked(image.Width * image.Height);
    var alpha = new byte[pixelCount];
    var hasTransparency = false;
    for (var i = 0; i < pixelCount; ++i) {
      var value = image.PixelData[i * 4 + 3];
      alpha[i] = value;
      hasTransparency |= value != byte.MaxValue;
    }
    return hasTransparency ? alpha : null;
  }

  private static double _ComputePsnr(byte[] reference, byte[] decoded) {
    if (reference.Length != decoded.Length)
      throw new InvalidOperationException("VP8 PSNR measurement buffers have different lengths.");

    double sse = 0;
    for (var i = 0; i < reference.Length; ++i) {
      var delta = reference[i] - decoded[i];
      sse += delta * delta;
    }
    if (sse == 0)
      return double.PositiveInfinity;

    var mse = sse / reference.Length;
    return 10.0 * Math.Log10(255.0 * 255.0 / mse);
  }

  private static void _Validate(WebPLossyEncodingOptions options) {
    if (options.Quality is < 0 or > 100)
      throw new ArgumentOutOfRangeException(nameof(options), "Quality must be in the range 0..100.");
    if (options.MinQuality is < 0 or > 100)
      throw new ArgumentOutOfRangeException(nameof(options), "MinQuality must be in the range 0..100.");
    if (options.MaxQuality is < 0 or > 100)
      throw new ArgumentOutOfRangeException(nameof(options), "MaxQuality must be in the range 0..100.");
    if (options.MinQuality > options.MaxQuality)
      throw new ArgumentException("MinQuality must not exceed MaxQuality.", nameof(options));
    if (options.Passes is < 1 or > 10)
      throw new ArgumentOutOfRangeException(nameof(options), "Passes must be in the range 1..10.");
    if (options.TokenPartitions is not (1 or 2 or 4 or 8))
      throw new ArgumentOutOfRangeException(nameof(options), "TokenPartitions must be 1, 2, 4, or 8.");
    if (options.TargetSizeBytes is <= 0)
      throw new ArgumentOutOfRangeException(nameof(options), "TargetSizeBytes must be positive.");
    if (options.TargetPsnr is double psnr && (!double.IsFinite(psnr) || psnr <= 0))
      throw new ArgumentOutOfRangeException(nameof(options), "TargetPsnr must be a finite positive value.");
    if (options.TargetSizeBytes is not null && options.TargetPsnr is not null)
      throw new ArgumentException("Specify either TargetSizeBytes or TargetPsnr, not both.", nameof(options));
  }
}
