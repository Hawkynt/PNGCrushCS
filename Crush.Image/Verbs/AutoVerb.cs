using CommandLine;
using Crush.Core;

namespace Crush.Image.Verbs;

[Verb("auto", isDefault: true, HelpText = "Auto-detect format and try all viable conversions for smallest output")]
public sealed class AutoVerb : ICrushOptions {

  [Option('i', "input", Required = true, HelpText = "Input image file path")]
  public string InputFile { get; set; } = "";

  [Option('o', "output", Required = true, HelpText = "Output image file path")]
  public string OutputFile { get; set; } = "";

  [Option("convert", Default = true, HelpText = "Try converting to other formats for smaller output")]
  public bool AllowConversion { get; set; } = true;

  [Option("lossy", Default = false, HelpText = "Allow lossy compression (JPEG, GIF quantization)")]
  public bool AllowLossy { get; set; } = false;

  [Option("strip", Default = false, HelpText = "Strip metadata from output")]
  public bool StripMetadata { get; set; } = false;

  [Option("auto-extension", Default = true, HelpText = "Automatically change output extension to match best format")]
  public bool AutoExtension { get; set; } = true;

  [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
  public int ParallelTasks { get; set; } = 0;

  [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
  public bool Verbose { get; set; } = false;
}
