using CommandLine;

namespace PngOptimizer;

public static partial class Program {
  /// <summary>Command line options class</summary>
  public class CommandLineOptions {
    [Option('i', "input", Required = true, HelpText = "Input PNG file path")]
    public string InputFile { get; set; } = "";

    [Option('o', "output", Required = true, HelpText = "Output PNG file path")]
    public string OutputFile { get; set; } = "";

    [Option('a', "auto-color-mode", Default = true, HelpText = "Automatically select best color mode")]
    public bool AutoColorMode { get; set; } = true;

    [Option("interlace", Default = false, HelpText = "Try interlaced PNG encoding")]
    public bool TryInterlacing { get; set; } = false;

    [Option('p', "partition", Default = true, HelpText = "Try smart partitioning for better compression")]
    public bool TryPartitioning { get; set; } = true;

    [Option('f', "filters", Default = "SingleFilter,ScanlineAdaptive,PartitionOptimized",
      HelpText = "Filter strategies to try (comma-separated)")]
    public string FilterStrategies { get; set; } = "SingleFilter,ScanlineAdaptive,PartitionOptimized";

    [Option('d', "deflate", Default = "Fastest,Default,Ultra",
      HelpText = "Deflate methods to try (comma-separated)")]
    public string DeflateMethods { get; set; } = "Fastest,Fast,Default,Maximum,Ultra";

    [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
    public int ParallelTasks { get; set; } = 0;

    [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
    public bool Verbose { get; set; } = false;
  }
}
