using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Crush.Core;
using Crush.Image.Verbs;
using FileFormat.Bmp;
using FileFormat.Tga;
using FileFormat.Tiff;
using Optimizer.Ani;
using Optimizer.Bmp;
using Optimizer.Cur;
using Optimizer.Gif;
using Optimizer.Ico;
using Optimizer.Image;
using Optimizer.Jpeg;
using Optimizer.Pcx;
using Optimizer.Png;
using Optimizer.Tga;
using Optimizer.Tiff;
using Optimizer.WebP;

namespace Crush.Image;

public static class Program {

  public static async Task<int> Main(string[] args) =>
    await Parser.Default
      .ParseArguments<AutoVerb, PngVerb, GifVerb, TiffVerb, BmpVerb, TgaVerb, PcxVerb,
        JpegVerb, IcoVerb, CurVerb, AniVerb, WebPVerb>(args)
      .MapResult(
        (AutoVerb v) => _RunAuto(v),
        (PngVerb v) => _RunPng(v),
        (GifVerb v) => _RunGif(v),
        (TiffVerb v) => _RunTiff(v),
        (BmpVerb v) => _RunBmp(v),
        (TgaVerb v) => _RunTga(v),
        (PcxVerb v) => _RunPcx(v),
        (JpegVerb v) => _RunJpeg(v),
        (IcoVerb v) => _RunIco(v),
        (CurVerb v) => _RunCur(v),
        (AniVerb v) => _RunAni(v),
        (WebPVerb v) => _RunWebP(v),
        _ => Task.FromResult(1)
      );

  private static Task<int> _RunAuto(AutoVerb opts) {
    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      StripMetadata: opts.StripMetadata,
      MaxParallelTasks: opts.ParallelTasks
    );

    return _RunOptimizer(opts, options, opts.AutoExtension);
  }

  private static Task<int> _RunPng(PngVerb opts) {
    var filterStrategies = _ParseEnumList<FilterStrategy>(opts.FilterStrategies);
    var deflateMethods = _ParseEnumList<DeflateMethod>(opts.DeflateMethods);

    var pngOptions = new PngOptimizationOptions {
      AutoSelectColorMode = opts.AutoColorMode,
      TryInterlacing = opts.TryInterlacing,
      TryPartitioning = opts.TryPartitioning,
      AllowLossyPalette = opts.LossyPalette,
      UseDithering = opts.UseDithering,
      IsHighQualityQuantization = opts.HighQualityQuantize,
      QuantizerNames = _ParseNames(opts.Quantizers),
      DithererNames = _ParseNames(opts.Ditherers),
      FilterStrategies = filterStrategies,
      DeflateMethods = deflateMethods,
      PreserveAncillaryChunks = opts.PreserveChunks,
      MaxParallelTasks = opts.ParallelTasks <= 0 ? Environment.ProcessorCount : opts.ParallelTasks,
    };

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.Png,
      MaxParallelTasks: opts.ParallelTasks,
      PngOptions: pngOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static Task<int> _RunGif(GifVerb opts) {
    var strategies = _ParseEnumList<PaletteReorderStrategy>(opts.PaletteStrategies);
    if (opts.CompressionPalette && !strategies.Contains(PaletteReorderStrategy.CompressionOptimized))
      strategies.Add(PaletteReorderStrategy.CompressionOptimized);

    var gifOptions = new GifOptimizationOptions(
      strategies,
      OptimizeDisposal: opts.OptimizeDisposal,
      TrimMargins: opts.TrimMargins,
      TryDeferredClear: opts.DeferredClear,
      DeduplicateFrames: opts.Deduplicate,
      TryFrameDifferencing: opts.FrameDiff,
      MaxParallelTasks: opts.ParallelTasks
    );

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.Gif,
      MaxParallelTasks: opts.ParallelTasks,
      GifOptions: gifOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static Task<int> _RunTiff(TiffVerb opts) {
    var compressions = _ParseEnumList<TiffCompression>(opts.Compressions);
    var predictors = _ParseEnumList<TiffPredictor>(opts.Predictors);
    var tileSizes = _ParseIntList(opts.TileSizes).Where(s => s >= 16 && s % 16 == 0).ToList();

    var tiffOptions = new TiffOptimizationOptions(
      compressions,
      predictors,
      AutoSelectColorMode: opts.AutoColorMode,
      DynamicStripSizing: opts.DynamicStripSizing,
      TryTiles: opts.TryTiles,
      TileSizes: tileSizes.Count > 0 ? tileSizes : null,
      MaxParallelTasks: opts.ParallelTasks
    );

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.Tiff,
      MaxParallelTasks: opts.ParallelTasks,
      TiffOptions: tiffOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static Task<int> _RunBmp(BmpVerb opts) {
    var compressions = _ParseEnumList<BmpCompression>(opts.Compressions);

    var bmpOptions = new BmpOptimizationOptions(
      Compressions: compressions,
      AutoSelectColorMode: opts.AutoColorMode,
      MaxParallelTasks: opts.ParallelTasks
    );

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.Bmp,
      MaxParallelTasks: opts.ParallelTasks,
      BmpOptions: bmpOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static Task<int> _RunTga(TgaVerb opts) {
    var compressions = _ParseEnumList<TgaCompression>(opts.Compressions);

    var tgaOptions = new TgaOptimizationOptions(
      Compressions: compressions,
      AutoSelectColorMode: opts.AutoColorMode,
      MaxParallelTasks: opts.ParallelTasks
    );

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.Tga,
      MaxParallelTasks: opts.ParallelTasks,
      TgaOptions: tgaOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static Task<int> _RunPcx(PcxVerb opts) {
    var pcxOptions = new PcxOptimizationOptions(
      AutoSelectColorMode: opts.AutoColorMode,
      MaxParallelTasks: opts.ParallelTasks
    );

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.Pcx,
      MaxParallelTasks: opts.ParallelTasks,
      PcxOptions: pcxOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static Task<int> _RunJpeg(JpegVerb opts) {
    var qualities = _ParseIntList(opts.Qualities).Where(q => q >= 1 && q <= 100).ToList();

    var jpegOptions = new JpegOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      MinQuality: opts.MinQuality,
      Qualities: qualities.Count > 0 ? qualities : null,
      StripMetadata: opts.StripMetadata,
      MaxParallelTasks: opts.ParallelTasks
    );

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.Jpeg,
      MaxParallelTasks: opts.ParallelTasks,
      JpegOptions: jpegOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static Task<int> _RunIco(IcoVerb opts) {
    var icoOptions = new IcoOptimizationOptions(MaxParallelTasks: opts.ParallelTasks);

    var options = new ImageOptimizationOptions(
      AllowFormatConversion: false,
      ForceFormat: Optimizer.Image.ImageFormat.Ico,
      MaxParallelTasks: opts.ParallelTasks,
      IcoOptions: icoOptions
    );

    return _RunOptimizer(opts, options, false);
  }

  private static Task<int> _RunCur(CurVerb opts) {
    var curOptions = new CurOptimizationOptions(MaxParallelTasks: opts.ParallelTasks);

    var options = new ImageOptimizationOptions(
      AllowFormatConversion: false,
      ForceFormat: Optimizer.Image.ImageFormat.Cur,
      MaxParallelTasks: opts.ParallelTasks,
      CurOptions: curOptions
    );

    return _RunOptimizer(opts, options, false);
  }

  private static Task<int> _RunAni(AniVerb opts) {
    var aniOptions = new AniOptimizationOptions(MaxParallelTasks: opts.ParallelTasks);

    var options = new ImageOptimizationOptions(
      AllowFormatConversion: false,
      ForceFormat: Optimizer.Image.ImageFormat.Ani,
      MaxParallelTasks: opts.ParallelTasks,
      AniOptions: aniOptions
    );

    return _RunOptimizer(opts, options, false);
  }

  private static Task<int> _RunWebP(WebPVerb opts) {
    var webpOptions = new WebPOptimizationOptions(
      MaxParallelTasks: opts.ParallelTasks,
      StripMetadata: opts.StripMetadata
    );

    var options = new ImageOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      AllowFormatConversion: opts.AllowConversion,
      ForceFormat: opts.AllowConversion ? null : Optimizer.Image.ImageFormat.WebP,
      MaxParallelTasks: opts.ParallelTasks,
      StripMetadata: opts.StripMetadata,
      WebPOptions: webpOptions
    );

    return _RunOptimizer(opts, options, opts.AllowConversion);
  }

  private static async Task<int> _RunOptimizer(ICrushOptions opts, ImageOptimizationOptions options, bool autoExtension) {
    try {
      var inputFile = new FileInfo(opts.InputFile);
      var optimizer = new ImageOptimizer(inputFile, options);

      return await CrushRunner.RunAsync(
        "Crush - Universal Image Optimizer v1.0",
        opts.InputFile,
        opts.OutputFile,
        opts.Verbose,
        async (ct, progress) => await optimizer.OptimizeAsync(ct, progress),
        r => r.FileContents,
        r => _ResolveOutputFile(opts.OutputFile, r, autoExtension),
        r => opts.Verbose
          ? $"\nFormat: {r.OriginalFormat} -> {r.OutputFormat}\n{r.Details}"
          : string.Empty,
        null
      );
    } catch (Exception ex) {
      Console.Error.WriteLine($"Error: {ex.Message}");
      if (opts.Verbose)
        Console.Error.WriteLine(ex.StackTrace);

      return 1;
    }
  }

  private static FileInfo _ResolveOutputFile(string outputPath, ImageOptimizationResult result, bool autoExtension) {
    if (!autoExtension || string.IsNullOrEmpty(result.OutputExtension))
      return new FileInfo(outputPath);

    var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";
    var nameWithoutExt = Path.GetFileNameWithoutExtension(outputPath);
    return new FileInfo(Path.Combine(dir, nameWithoutExt + result.OutputExtension));
  }

  private static List<T> _ParseEnumList<T>(string input) where T : struct, Enum {
    var result = new List<T>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (Enum.TryParse<T>(part, true, out var value))
        result.Add(value);

    return result;
  }

  private static List<int> _ParseIntList(string input) {
    var result = new List<int>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (int.TryParse(part, out var value))
        result.Add(value);

    return result;
  }

  private static List<string> _ParseNames(string input) =>
    input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
