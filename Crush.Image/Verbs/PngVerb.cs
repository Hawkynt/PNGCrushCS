using CommandLine;
using Crush.Core;

namespace Crush.Image.Verbs;

[Verb("png", HelpText = "Optimize as PNG with format-specific options")]
public sealed class PngVerb : ICrushOptions {

  [Option('i', "input", Required = true, HelpText = "Input image file path")]
  public string InputFile { get; set; } = "";

  [Option('o', "output", Required = true, HelpText = "Output PNG file path")]
  public string OutputFile { get; set; } = "";

  [Option('a', "auto-color-mode", Default = true, HelpText = "Automatically select best color mode")]
  public bool AutoColorMode { get; set; } = true;

  [Option("interlace", Default = true, HelpText = "Try interlaced PNG encoding")]
  public bool TryInterlacing { get; set; } = true;

  [Option('p', "partition", Default = true, HelpText = "Try smart partitioning for better compression")]
  public bool TryPartitioning { get; set; } = true;

  [Option('f', "filters", Default = "SingleFilter,ScanlineAdaptive,PartitionOptimized",
    HelpText = "Filter strategies to try (comma-separated)")]
  public string FilterStrategies { get; set; } = "SingleFilter,ScanlineAdaptive,PartitionOptimized";

  [Option('d', "deflate", Default = "Fastest,Default,Ultra",
    HelpText = "Deflate methods to try (comma-separated)")]
  public string DeflateMethods { get; set; } = "Fastest,Default,Ultra";

  [Option("lossy-palette", Default = false, HelpText = "Allow lossy palette quantization")]
  public bool LossyPalette { get; set; } = false;

  [Option("dithering", Default = false, HelpText = "Try multiple quantizer/ditherer combos")]
  public bool UseDithering { get; set; } = false;

  [Option("quantizers", Default = "Wu,Octree,MedianCut",
    HelpText = "Quantizers to try (comma-separated)")]
  public string Quantizers { get; set; } = "Wu,Octree,MedianCut";

  [Option("ditherers", Default = "None,FloydSteinberg",
    HelpText = "Ditherers to try (comma-separated)")]
  public string Ditherers { get; set; } = "None,FloydSteinberg";

  [Option("hq-quantize", Default = false, HelpText = "Use high-quality quantization")]
  public bool HighQualityQuantize { get; set; } = false;

  [Option("preserve-chunks", Default = false, HelpText = "Preserve ancillary PNG chunks")]
  public bool PreserveChunks { get; set; } = false;

  [Option("convert", Default = false, HelpText = "Also try other formats")]
  public bool AllowConversion { get; set; } = false;

  [Option("lossy", Default = false, HelpText = "Allow lossy compression")]
  public bool AllowLossy { get; set; } = false;

  [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
  public int ParallelTasks { get; set; } = 0;

  [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
  public bool Verbose { get; set; } = false;
}
