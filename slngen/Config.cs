using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace SlnGen
{
    public class Config : ProgramArguments
    {
        private static ProgramArgumentInfo[] infos = new ProgramArgumentInfo[] {
            
            // collection of project files and/or directories
            new ProgramArgumentInfo() {
                Name = "files",
                IsPositional = true,
                Type = typeof(string[]),
                Description = SlnGenStrings.fileUsage,
                Default = new string[0],
            },
            new ProgramArgumentInfo() {
                Name = "recurse",
                ShortForms = { "s" },
                Description = SlnGenStrings.recurseUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "hive",
                Description = SlnGenStrings.hiveUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "nest",
                ShortForms = { "n" },
                Description = SlnGenStrings.nestUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "noLaunch",
                ShortForms = { "no" },
                Description = SlnGenStrings.noLaunchUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "noSkipRefs",
                ShortForms = { "nsr" },
                Description = SlnGenStrings.noSkipRefsUsage,
                Default = true,
            },
            new ProgramArgumentInfo() {
                Name = "run",
                Type = typeof(string),
                Description = SlnGenStrings.runUsage,
                Default = null,
            },
            new ProgramArgumentInfo() {
                Name = "sourceDepot",
                ShortForms = { "sd" },
                Description = SlnGenStrings.sourceDepotUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "outputFile",
                ShortForms = { "o" },
                Type = typeof(string),
                Description = SlnGenStrings.outputUsage,
                Default = null,
            },
            new ProgramArgumentInfo() {
                Name = "useCurrent",
                ShortForms = { "w" },
                Description = SlnGenStrings.useCurrentUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "quiet",
                ShortForms = { "q" },
                Type = typeof(bool[]),
                Description = SlnGenStrings.quietUsage,
                Default = new bool[0],
            },
            new ProgramArgumentInfo() {
                Name = "verbose",
                ShortForms = { "v" },
                Type = typeof(bool[]),
                Description = SlnGenStrings.verboseUsage,
                Default = new bool[0],
            },
            new ProgramArgumentInfo() {
                Name = "validate",
                Description = SlnGenStrings.validateUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "devenv",
                Type = typeof(string),
                Description = SlnGenStrings.devenvUsage,
                Default = null,
            },
            new ProgramArgumentInfo() {
                Name = "vsver",
                Type = typeof(string),
                Description = SlnGenStrings.vsverUsage,
                Default = "0",
            },
            new ProgramArgumentInfo() {
                Name = "error",
                Type = typeof(string[]),
                Description = SlnGenStrings.errorUsage,
                Default = new string[0],
            },
            new ProgramArgumentInfo() {
                Name = "addSafeImports",
                ShortForms = { "asi" },
                Type = typeof(bool),
                Description = SlnGenStrings.addSafeImportsUsage,
                Default = false,
            },
            new ProgramArgumentInfo() {
                Name = "vsmsbuild",
                Type = typeof(bool),
                Description = SlnGenStrings.vsmsbuildUsage,
                Default = false,
            },
            new ProgramArgumentInfo {
                Name = "fixToolsVersion",
                Type = typeof(bool),
                Description = SlnGenStrings.fixToolsVersionUsage,
                Default= null,
            },
            new ProgramArgumentInfo {
                Name = "purge",
                Type = typeof(bool),
                Description = SlnGenStrings.purgeUsage,
                Default = false,
            },
        };

        public enum Verbosity
        {
            Quiet = -1,
            Normal = 0,
            Verbose = 1,
            ReallyVerbose = 2,
        }

        private string _slnFile = null;
        private List<string> _initialProjects = new List<string>();
        private const string _tempSolutionDirPrefix = "sln_";

        public bool launchVS { get { return !(GetValue<bool>("noLaunch") || validateOnly); } }
        public bool recurse { get { return GetValue<bool>("recurse"); } }
        public bool inHive { get { return GetValue<bool>("hive"); } }
        public bool skipRefs { get { return !GetValue<bool>("noSkipRefs"); } }
        public bool useCurrentDirectory { get { return GetValue<bool>("useCurrent"); } }
        public Verbosity verbosity { get; internal set; }
        public bool run { get { return GetValue<string>("run") != null; } }
        public string projectConfiguration { get { return GetValue<string>("run"); } }
        public bool useSourceDepot { get { return GetValue<bool>("sourceDepot") ; } }
        public bool validateOnly { get { return GetValue<bool>("validate"); } }
        public bool nest { get { return GetValue<bool>("nest"); } }
        public bool addSafeImports { get { return GetValue<bool>("addSafeImports"); } }
        public bool vsmsbuild { get { return GetValue<bool>("vsmsbuild"); } }
        public Program.Status status { get; internal set; }
        public VisualStudioVersion targetVersion 
        { 
            get 
            {
                var ver = GetValue<string>("vsver");

                if (ver == "0") // Auto detect
                {
                    if (Registry.GetValue(@"HKEY_CLASSES_ROOT\VisualStudio.sln.14.0", null, null) != null)
                    {
                        ver = "14";
                    }
                    else if (Registry.GetValue(@"HKEY_CLASSES_ROOT\VisualStudio.sln.11.0", null, null) != null)
                    {
                        ver = "11";
                    }
                    else if (Registry.GetValue(@"HKEY_CLASSES_ROOT\VisualStudio.sln.10.0", null, null) != null)
                    {
                        ver = "10";
                    }
                    else if (Registry.GetValue(@"HKEY_CLASSES_ROOT\VisualStudio.sln.12.0", null, null) != null)
                    {
                        ver = "12";
                    }
                    else // looks like no VS installed. By default we choose VS 2010
                    {
                        ver = "14";
                    }
                    SetValue("vsver", ver);
                }

                if (ver == "12")
                    return VisualStudioVersion.VS12;
                else if (ver == "11")
                    return VisualStudioVersion.VS11;
                else
                    return VisualStudioVersion.VS14;
            } 
        }
        public bool fixToolsVersion 
        { 
            get 
            { 
                bool? arg = GetValue<bool?>("fixToolsVersion"); 
                if (arg.HasValue) { return arg.Value; }

                return false;
            } 
        }
        public bool purge { get { return GetValue<bool>("purge"); } }

        public string slnFile
        {
            get { return _slnFile; }
            internal set { if (_slnFile == null) _slnFile = value; }
        }

        public string currentDirectory { get; internal set; }
        public List<string> initialProjects { get { return _initialProjects; } }

        public string devenvArgs
        {
            get
            {
                StringBuilder procArgs = new StringBuilder();
                if (inHive)
                {
                    procArgs.Append("/rootSuffix Exp /RANU ");
                }

                if (run)
                {
                    procArgs.Append("/run ");
                    procArgs.Append(projectConfiguration);
                    procArgs.Append(" ");
                }

                if (useSourceDepot)
                {
                    procArgs.Append("/command \"File.UseSourceControlwithSolution\" ");
                }

                procArgs.Append('"');
                procArgs.Append(slnFile);
                procArgs.Append('"');
                return procArgs.ToString();
            }
        }

        public string devenvExe
        {
            get
            {
                // If user specify the devenv.exe, return it regardless
                string explicitDevenvExe = this.ExplicitDevenvExe;
                if (!String.IsNullOrEmpty(explicitDevenvExe)) 
                {
                    return explicitDevenvExe;
                }

                List<string> values = Util.ExtractQouted(Environment.GetEnvironmentVariable("VSDEVENV"));
                if (values.Count != 0 && File.Exists(values[0])) return Path.GetFullPath(values[0]);

                // Try to look at the place devenv.exe suppose to be first
                string default_result = "devenv.exe";
                string vsPath = null;
                if (this.targetVersion == VisualStudioVersion.VS10)
                {
                    vsPath = Environment.GetEnvironmentVariable("VS100COMNTOOLS");
                }
                else if (this.targetVersion == VisualStudioVersion.VS11)
                {
                    vsPath = Environment.GetEnvironmentVariable("VS110COMNTOOLS");
                }
                else if (this.targetVersion == VisualStudioVersion.VS12)
                {
                    vsPath = Environment.GetEnvironmentVariable("VS120COMNTOOLS");
                }
                else
                {
                    vsPath = Environment.GetEnvironmentVariable("VS140COMNTOOLS");
                }
                if (!String.IsNullOrEmpty(vsPath))
                {
                    var dirInfo = new DirectoryInfo(vsPath);
                    DirectoryInfo vsDirInfo = dirInfo.Parent;
                    vsPath = Path.Combine(vsDirInfo.FullName, "IDE");
                    default_result = Path.Combine(vsPath, "devenv.exe");
                }

                // If can't find it, look at magical places
                if (!File.Exists(default_result))
                {
                    values = Util.ExtractQouted((string)Registry.GetValue(
                        "HKEY_CLASSES_ROOT\\VisualStudio.Launcher.sln\\Shell\\Open\\Command", null, null
                    ));
                    if (values.Count != 0) return Path.GetFullPath(values[0]);

                    // Giving up.
                    if (verbosity > Verbosity.Quiet)
                    {
                        Console.WriteLine(@"
Can't find a specified path to devenv.exe or a VS launcher.
Tried: command line, VSDEVENV environment variable, and
       VisualStudio.Launcher.sln reg key.
Will try looking in path for devenv.exe.");
                    }
                }
                return default_result;
            }
        }

        public string ExplicitDevenvExe
        {
            get
            {
                List<string> values = Util.ExtractQouted(GetValue<string>("devenv"));
                if (values.Count != 0)
                {
                    return Path.GetFullPath(values[0]);
                }
                else
                {
                    return null;
                }
            }
        }

        public string vsmsbuildArgs
        {
            get
            {
                StringBuilder procArgs = new StringBuilder();
                
                procArgs.Append("/project:\"");
                procArgs.Append(slnFile);
                procArgs.Append("\" ");

                string devEnvExe = this.ExplicitDevenvExe;
                if (devEnvExe != null)
                {
                    procArgs.Append("/devenv:\"");
                    procArgs.Append(devEnvExe);
                    procArgs.Append('"');
                }
                return procArgs.ToString();
            }
        }

        public Config(string[] args) : base(args, infos)
        {
            status = Program.Status.OK;

            int verbose = 0;
            foreach (bool delta in GetValue<bool[]>("verbose"))
                verbose += delta ? 1 : -1;

            foreach (bool delta in GetValue<bool[]>("quiet"))
                verbose -= delta ? 1 : -1;

            verbosity = Verbosity.Normal;
            if (verbose < 0)
                verbosity = Verbosity.Quiet;
            else if (verbose == 1)
                verbosity = Verbosity.Verbose;
            else if (verbose > 1)
                verbosity = Verbosity.ReallyVerbose;

            string outputFile = GetValue<string>("outputFile");
            if (outputFile != null)
                slnFile = Config.CreateSlnFilename(Path.GetDirectoryName(outputFile), outputFile);

            if (UsageException != null)
                status = Program.Status.badOrMissingArgumentError;

            initialProjects.AddRange(GetValue<string[]>("files"));
            currentDirectory = Directory.GetCurrentDirectory();

            foreach (string error in GetValue<string[]>("error"))
                SlnError.ChangeEnabled(error);
        }

        public Config(string[] args, string fakeCurrentDirectory) : this(args)
        {
            currentDirectory = fakeCurrentDirectory;
        }

        /// <summary>
        /// Attempts to apply sane defaults for unspecified behavior.
        /// </summary>
        public void guessDefaults()
        {
            if (initialProjects.Count == 0)
            {
                if (recurse)
                {
                    // We are doing recursion on an unspecified directory (this directory).
                    initialProjects.Add(currentDirectory);
                }
                else
                {
                    // We need to find an unspecified file.
                    string[] projFiles = Directory.GetFiles(currentDirectory, "*.*proj");
                    initialProjects.AddRange(projFiles);
                }
            }

            // Attempt to determine where we should put our output.
            if (slnFile == null)
            {
                if (!useCurrentDirectory)
                {
                    // We are creating a scratch project. Don't need a specific file name.
                    CreateTargetSlnFilename();
                }
                else if (initialProjects.Count == 1)
                {
                    // We've been given a single argument, use that as the base.
                    SetSlnFileByName(initialProjects[0]);
                }
            }
        }

        /// <summary>
        /// Creates the .sln target file name
        /// </summary>
        static public string CreateSlnFilename(string targetDirectory, string slnFilename)
        {
            string baseDir = Path.GetFileName(Environment.GetEnvironmentVariable("BASEDIR"));
            string file = Path.GetFileNameWithoutExtension(slnFilename) + 
                            (!string.IsNullOrWhiteSpace(baseDir) ? (" [" + baseDir + "]") : "") + 
                            ".sln";
            return Path.Combine(targetDirectory, file);
        }

        /// <summary>
        /// Sets the .sln target file name, if necessary
        /// </summary>
        private void SetSlnFileByName(string baseName)
        {
            if (slnFile != null)
                return; // our work is done here.

            slnFile = CreateSlnFilename(currentDirectory, baseName);
            Directory.CreateDirectory(Path.GetDirectoryName(slnFile));
        }

        /// <summary>
        /// Creates a .sln file name in target, if necessary
        /// </summary>
        private void CreateTargetSlnFilename()
        {
            if (slnFile != null)
                return; // our work is done here.

            string guess = "Project";

            if (initialProjects.Count > 0)
                guess = initialProjects[0];

            // Create a temporary location for the solution file.
            string rootDir = Environment.GetEnvironmentVariable("INETROOT", EnvironmentVariableTarget.Process);
            slnFile = Config.CreateSlnFilename(Path.Combine(rootDir, "target", "Solutions"), guess);

            Directory.CreateDirectory(Path.GetDirectoryName(slnFile));
        }

        public static string GetDefaultSolutionDirectory()
        {
            string rootDir = Environment.GetEnvironmentVariable("INETROOT", EnvironmentVariableTarget.Process);
            return Path.Combine(rootDir, "target", "Solutions");
        }

        public static void CleanupDefaultSolutionDirectory()
        {
            string cleanupDirectory = GetDefaultSolutionDirectory();

            try
            {
                Directory.Delete(cleanupDirectory, true);
            }
            catch (Exception e)
            {
                Console.WriteLine("Can't delete directory {0}: {1}", Path.GetFileName(cleanupDirectory), e.Message);
            }
        }
    }
}
