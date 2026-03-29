using CommandLine;
using Crush.Core;

namespace Crush.Image.Verbs;

[Verb("ico", HelpText = "Optimize ICO files")]
public sealed class IcoVerb : ICrushOptions {

  [Option('i', "input", Required = true, HelpText = "Input ICO file path")]
  public string InputFile { get; set; } = "";

  [Option('o', "output", Required = true, HelpText = "Output ICO file path")]
  public string OutputFile { get; set; } = "";

  [Option('j', "jobs", Default = 0, HelpText = "Maximum number of parallel tasks (0 = use all cores)")]
  public int ParallelTasks { get; set; } = 0;

  [Option('v', "verbose", Default = false, HelpText = "Enable verbose output")]
  public bool Verbose { get; set; } = false;
}
