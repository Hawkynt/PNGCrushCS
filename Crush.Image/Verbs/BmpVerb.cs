using CommandLine;
using Crush.Core;

namespace Crush.Image.Verbs;

[Verb("bmp", HelpText = "Optimize as BMP with format-specific options")]
public sealed class BmpVerb : ICrushOptions {

  [Option('i', "input", Required = true, HelpText = "Input image file path")]
  public string InputFile { get; set; } = "";

  [Option('o', "output", Required = true, HelpText = "Output BMP file path")]
  public string OutputFile { get; set; } = "";

  [Option('c', "compression", Default = "None,Rle8,Rle4", HelpText = "Compression methods (comma-separated)")]
  public string Compressions { get; set; } = "None,Rle8,Rle4";

  [Option('a', "auto-color-mode", Default = true, HelpText = "Automatically select best color mode")]
  public bool AutoColorMode { get; set; } = true;

  [Option("convert", Default = false, HelpText = "Also try other formats")]
  public bool AllowConversion { get; set; } = false;

  [Option("lossy", Default = false, HelpText = "Allow lossy compression")]
  public bool AllowLossy { get; set; } = false;

  [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
  public int ParallelTasks { get; set; } = 0;

  [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
  public bool Verbose { get; set; } = false;
}
