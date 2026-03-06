using CommandLine;
using Crush.Core;

namespace Optimizer.WebP;

public static partial class Program {
  public class CommandLineOptions : ICrushOptions {
    [Option('i', "input", Required = true, HelpText = "Input WebP file path")]
    public string InputFile { get; set; } = "";

    [Option('o', "output", Required = true, HelpText = "Output WebP file path")]
    public string OutputFile { get; set; } = "";

    [Option('s', "strip-metadata", Default = true, HelpText = "Strip metadata (EXIF, ICCP, XMP) from the output")]
    public bool StripMetadata { get; set; } = true;

    [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
    public int ParallelTasks { get; set; } = 0;

    [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
    public bool Verbose { get; set; } = false;
  }
}
