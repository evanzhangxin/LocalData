namespace SlnGen
{
    using System;
    using System.Collections.Generic;

    public class Program
    {
        private Config _config = null;
        private ProjectClosure _closure = null;

        public enum Status
        {
            OK = 0,
            noProjectsFoundError = -1,
            tooManyProjectsFoundError = -2,
            badOrMissingArgumentError = -3,
            badProjectNameError = -4,
            validationError = -6,
        }

        public ProjectClosure closure { get { return _closure; } }

        static int Main(string[] args)
        {
            SlnError.Init();

            Config config = new Config(args);
            if (config.ShouldShowUsageAndExit)
            {
                Console.WriteLine(config.Usage);

                Console.Write("The available error condition checks are:");
                foreach (string name in SlnError.names)
                    Console.Write(" {0}", name);
                Console.WriteLine();

                return (int)config.status;
            }

            if (config.verbosity >= Config.Verbosity.Verbose)
            {
                Console.Write("Looking for the following error conditions:");
                foreach (SlnError.ErrorID id in SlnError.enabled.Keys)
                    Console.Write(" {0}", id);
                Console.WriteLine();
            }

            Program program = new Program(config);

            if (config.validateOnly) return (int)program.RunValidation();

            return (int)program.Run();
        }

        public Program(Config config)
        {
            _config = config;
            _closure = new ProjectClosure(_config);
        }

        /// <summary>
        /// Our main entry point
        /// </summary>
        public Status Run()
        {
            if (_config.purge)
            {
                Console.WriteLine("Deleting old temporary slngen solutions...");
                Config.CleanupDefaultSolutionDirectory();
                Console.WriteLine("Deleted slngen solution directory.");
            }

            _config.guessDefaults();

            if (_config.slnFile == null)
            {
                Console.WriteLine("Error: Can't determine target project name, specify with -o");
                return Status.tooManyProjectsFoundError;
            }

            if (_config.initialProjects.Count == 0)
            {
                Console.WriteLine("There are no project files in this directory.");
                return Status.noProjectsFoundError;
            }

            Status result = _closure.AddEntriesToParseFiles(_config.initialProjects, _config.recurse);
            if (result != Status.OK) return result;

            _closure.ProcessProjectFiles();
            if (_config.fixToolsVersion) { _closure.FixToolVersions(); }

            // Print validation errors, if any
            SlnError.PrintErrors(_config.verbosity);

            if (_closure.ActualProjects.Count == 0)
            {
                Console.WriteLine("No projects to load.");
                return Status.noProjectsFoundError;
            }

            _closure.CreateTempSlnFile(_config.slnFile, _config.nest, _config.targetVersion);

            if (_closure.HasUnsafeImports)
            {
                if (_config.addSafeImports)
                {
                    _closure.AuthorizeUnsafeImports();
                }
                else
                {
                    Console.WriteLine("Warning, unsafe imports found.");
                    foreach (var item in _closure.UnsafeImports)
                    {
                        Console.WriteLine(" {0}", item);
                    }
                }
            }

            // Launch this Sln File in VS
            if (_config.launchVS)
            {
                try
                {
                    if (_config.verbosity >= Config.Verbosity.Verbose)
                        Console.WriteLine("Running: {0} {1}", _config.devenvExe, _config.devenvArgs);

                    System.Diagnostics.Process proc = new System.Diagnostics.Process();
                    if (!_config.vsmsbuild)
                    {
                        proc.StartInfo.FileName = _config.devenvExe;
                        proc.StartInfo.Arguments = _config.devenvArgs;
                    }
                    else
                    {

                        proc.StartInfo.FileName = "vsmsbuild.exe";
                        proc.StartInfo.Arguments = _config.vsmsbuildArgs;
                        proc.StartInfo.UseShellExecute = false;
                    }
                    proc.Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            return 0;
        }

        public Status RunValidation()
        {
            Status result = Status.OK;
            ProjectClosure guidCheck = new ProjectClosure(_config);
            guidCheck.AddEntriesToParseFiles(_config.initialProjects, _config.recurse);

            guidCheck.ProcessProjectFiles();

            SlnError.PrintErrors(_config.verbosity);
            if (SlnError.errors.Count > 0)
                result = Status.validationError;

            if ((_config.initialProjects.Count < 2) ||
                !SlnError.enabled.ContainsKey(SlnError.ErrorID.ProjectsOverlapped))
            {
                return result;
            }

            SlnError.errors.Clear();
            SlnError.enabled.Clear();
            SlnError.enabled[SlnError.ErrorID.ProjectsOverlapped] = SlnError.ErrorID.ProjectsOverlapped;

            SortedDictionary<string, ProjectClosure> overlap = new SortedDictionary<string, ProjectClosure>();
            foreach (string project in _config.initialProjects)
            {
                ProjectClosure closure = new ProjectClosure(_config);
                closure.AddEntriesToParseFiles(project, _config.recurse);
                closure.ProcessProjectFiles();

                foreach (string other in overlap.Keys)
                {
                    if (!closure.ValidateNoOverlap(project, overlap[other], other))
                        result = Status.validationError;
                }

                overlap.Add(project, closure);
            }

            SlnError.PrintErrors(_config.verbosity);
            return result;
        }

    }
}
