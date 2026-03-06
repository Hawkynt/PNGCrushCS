using CommandLine;
using Crush.Core;

namespace Crush.Jpeg;

public static partial class Program {
  public class CommandLineOptions : ICrushOptions {
    [Option('i', "input", Required = true, HelpText = "Input JPEG file path")]
    public string InputFile { get; set; } = "";

    [Option('o', "output", Required = true, HelpText = "Output JPEG file path")]
    public string OutputFile { get; set; } = "";

    [Option("lossy", Default = false, HelpText = "Enable lossy re-encoding mode")]
    public bool AllowLossy { get; set; } = false;

    [Option('q', "min-quality", Default = 75, HelpText = "Minimum quality for lossy mode (1-100)")]
    public int MinQuality { get; set; } = 75;

    [Option("qualities", Default = "75,80,85,90,95",
      HelpText = "Quality levels to try in lossy mode (comma-separated)")]
    public string Qualities { get; set; } = "75,80,85,90,95";

    [Option("strip", Default = true, HelpText = "Try stripping metadata (EXIF, ICC, comments)")]
    public bool StripMetadata { get; set; } = true;

    [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
    public int ParallelTasks { get; set; } = 0;

    [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
    public bool Verbose { get; set; } = false;
  }
}
