using CommandLine;
using Crush.Core;

namespace Crush.Image.Verbs;

[Verb("tiff", HelpText = "Optimize as TIFF with format-specific options")]
public sealed class TiffVerb : ICrushOptions {

  [Option('i', "input", Required = true, HelpText = "Input image file path")]
  public string InputFile { get; set; } = "";

  [Option('o', "output", Required = true, HelpText = "Output TIFF file path")]
  public string OutputFile { get; set; } = "";

  [Option('c', "compression", Default = "None,PackBits,Lzw,Deflate,DeflateUltra",
    HelpText = "Compression methods (comma-separated)")]
  public string Compressions { get; set; } = "None,PackBits,Lzw,Deflate,DeflateUltra";

  [Option("predictor", Default = "None,HorizontalDifferencing",
    HelpText = "Predictor modes (comma-separated)")]
  public string Predictors { get; set; } = "None,HorizontalDifferencing";

  [Option('a', "auto-color-mode", Default = true, HelpText = "Automatically select best color mode")]
  public bool AutoColorMode { get; set; } = true;

  [Option("dynamic-strips", Default = true, HelpText = "Dynamically generate strip sizes")]
  public bool DynamicStripSizing { get; set; } = true;

  [Option("tiles", Default = false, HelpText = "Try tiled TIFF encoding")]
  public bool TryTiles { get; set; } = false;

  [Option("tile-sizes", Default = "64,128,256", HelpText = "Tile sizes to try (comma-separated)")]
  public string TileSizes { get; set; } = "64,128,256";

  [Option("convert", Default = false, HelpText = "Also try other formats")]
  public bool AllowConversion { get; set; } = false;

  [Option("lossy", Default = false, HelpText = "Allow lossy compression")]
  public bool AllowLossy { get; set; } = false;

  [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
  public int ParallelTasks { get; set; } = 0;

  [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
  public bool Verbose { get; set; } = false;
}
