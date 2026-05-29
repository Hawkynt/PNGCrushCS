using Optimizer.Ani;
using Optimizer.Bmp;
using Optimizer.Cur;
using Optimizer.Gif;
using Optimizer.Ico;
using Optimizer.Jpeg;
using Optimizer.Pcx;
using Optimizer.Png;
using Optimizer.Tga;
using Optimizer.Tiff;
using Optimizer.WebP;

namespace Optimizer.Image;

/// <summary>Configuration for the universal image optimizer.</summary>
public sealed record ImageOptimizationOptions(
  bool AllowLossy = false,
  bool AllowFormatConversion = true,
  ImageFormat? ForceFormat = null,
  int MaxParallelTasks = 0,
  bool StripMetadata = false,
  PngOptimizationOptions? PngOptions = null,
  GifOptimizationOptions? GifOptions = null,
  TiffOptimizationOptions? TiffOptions = null,
  BmpOptimizationOptions? BmpOptions = null,
  TgaOptimizationOptions? TgaOptions = null,
  PcxOptimizationOptions? PcxOptions = null,
  JpegOptimizationOptions? JpegOptions = null,
  IcoOptimizationOptions? IcoOptions = null,
  CurOptimizationOptions? CurOptions = null,
  AniOptimizationOptions? AniOptions = null,
  WebPOptimizationOptions? WebPOptions = null
);
