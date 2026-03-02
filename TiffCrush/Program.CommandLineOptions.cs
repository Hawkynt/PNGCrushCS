using CommandLine;

namespace TiffOptimizer;

public static partial class Program {
  public class CommandLineOptions {
    [Option('i', "input", Required = true, HelpText = "Input TIFF file path")]
    public string InputFile { get; set; } = "";

    [Option('o', "output", Required = true, HelpText = "Output TIFF file path")]
    public string OutputFile { get; set; } = "";

    [Option('c', "compression", Default = "None,PackBits,Lzw,Deflate,DeflateUltra",
      HelpText = "Compression methods to try (comma-separated)")]
    public string Compressions { get; set; } = "None,PackBits,Lzw,Deflate,DeflateUltra";

    [Option("predictor", Default = "None,HorizontalDifferencing",
      HelpText = "Predictor modes to try (comma-separated)")]
    public string Predictors { get; set; } = "None,HorizontalDifferencing";

    [Option('a', "auto-color-mode", Default = true, HelpText = "Automatically select best color mode")]
    public bool AutoColorMode { get; set; } = true;

    [Option("dynamic-strips", Default = true, HelpText = "Dynamically generate strip sizes based on image height")]
    public bool DynamicStripSizing { get; set; } = true;

    [Option("tiles", Default = false, HelpText = "Try tiled TIFF encoding (beneficial for large images)")]
    public bool TryTiles { get; set; } = false;

    [Option("tile-sizes", Default = "64,128,256",
      HelpText = "Tile sizes to try in pixels (comma-separated, must be multiples of 16)")]
    public string TileSizes { get; set; } = "64,128,256";

    [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
    public int ParallelTasks { get; set; } = 0;

    [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
    public bool Verbose { get; set; } = false;
  }
}
