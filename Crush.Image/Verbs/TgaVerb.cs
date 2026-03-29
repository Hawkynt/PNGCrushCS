using CommandLine;
using Crush.Core;

namespace Crush.Image.Verbs;

[Verb("tga", HelpText = "Optimize as TGA with format-specific options")]
public sealed class TgaVerb : ICrushOptions {

  [Option('i', "input", Required = true, HelpText = "Input image file path")]
  public string InputFile { get; set; } = "";

  [Option('o', "output", Required = true, HelpText = "Output TGA file path")]
  public string OutputFile { get; set; } = "";

  [Option('c', "compression", Default = "None,Rle", HelpText = "Compression methods (comma-separated)")]
  public string Compressions { get; set; } = "None,Rle";

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
