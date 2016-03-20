namespace SlnGen
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Text;
    using System.Xml.Linq;
    using Microsoft.Build.Evaluation;
    using Microsoft.Win32;

    public class ProjectClosure
    {
        Config _config;

        const string _cloudServiceProjectGuid = "{CC5FD16D-436D-48AD-A40C-5A424C6E3E79}";
        const string _csharpProjectGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
        const string _databaseProjectGuid = "{C8D11400-126E-41CD-887F-60BD40844F9E}";
        const string _vbProjectGuid = "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}";
        const string _vcxProjectGuid = "{C33DA1E7-35A1-4C8C-86DA-3FE0A4A73B00}";
        const string _mProjectGuid = "{337e1157-e1f1-4e32-98cd-e7a5edbd4880}";
        const string _cosmosScopeProjectGuid = "{4A077C08-FBAA-4D03-AD18-518272689CD1}";

        Stack<string> _projectsToLoad = new Stack<string>();
        List<ProjectInfo> _actualProjects = new List<ProjectInfo>();
        List<string> _solutionItems = new List<string>();
        ICollection<string> unsafeImports;

        Dictionary<VSPath, ProjectInfo> _projectInfo = new Dictionary<VSPath, ProjectInfo>();
        Dictionary<string, ProjectInfo> _guidToProject = new Dictionary<string, ProjectInfo>();
        Dictionary<string, ProjectInfo> _nameToProject = new Dictionary<string, ProjectInfo>();

        public Stack<string> ProjectsToLoad { get { return _projectsToLoad; } }
        public List<ProjectInfo> ActualProjects { get { return _actualProjects; } }
        public List<string> SolutionItems { get { return _solutionItems; } }
        public Dictionary<VSPath, ProjectInfo> ProjectInfo { get { return _projectInfo; } }

        public Config Config { get { return _config; } }

        public Config.Verbosity Verbosity { get { return _config.verbosity; } }

        public NestingDir Nesting { get; internal set; }

        public ProjectClosure(Config config)
        {
            _config = config;
            Nesting = new NestingDir();
        }

        public enum FileType
        {
            Bad,
            File,
            Directory
        }

        /// <summary>
        /// Determines if a file/directory exists and appears to be accessable.
        /// </summary>
        public static FileType GetFileType(string projectFile)
        {
            try
            {
                var attributes = File.GetAttributes(projectFile);

                if ((attributes & FileAttributes.System) != 0) return FileType.Bad;
                if ((attributes & FileAttributes.Hidden) != 0) return FileType.Bad;
                if ((attributes & FileAttributes.Offline) != 0) return FileType.Bad;

                if ((attributes & FileAttributes.Directory) != 0) return FileType.Directory;

                return FileType.File;
            }
            catch
            {
                Console.WriteLine("Bad file or directory: {0}", projectFile);
                return FileType.Bad;
            }
        }

        /// <summary>
        /// Processes directories and sub-directories and loads all project files within them.
        /// </summary>
        public Program.Status AddEntriesToParseFiles(List<string> pathnames, bool recurse)
        {
            Program.Status result = Program.Status.OK;

            foreach (string pathname in pathnames)
            {
                Program.Status temp = AddEntriesToParseFiles(pathname, recurse);
                if (temp != Program.Status.OK)
                    result = temp;
            }

            return result;
        }

        /// <summary>
        /// Processes a directory and the sub-directories and loads all project files within them.
        /// </summary>
        public Program.Status AddEntriesToParseFiles(string pathname, bool recurse)
        {
            switch (GetFileType(pathname))
            {
                case FileType.Bad:
                    SlnError.ReportError(
                        SlnError.ErrorID.MissingFile, pathname, "Missing, system, or unreadable file encountered."
                    );
                    return Program.Status.badProjectNameError;

                case FileType.File:
                    _projectsToLoad.Push(pathname);
                    return Program.Status.OK;

                case FileType.Directory:
                    foreach (string file in Directory.GetFiles(pathname, "*.*proj"))
                        _projectsToLoad.Push(file);

                    if (recurse)
                        foreach (string childDir in Directory.GetDirectories(pathname))
                            AddEntriesToParseFiles(childDir, true);

                    return Program.Status.OK;

                default:
                    Console.WriteLine("Bad internal (SlnGen) state for {0}.", pathname);
                    return Program.Status.badProjectNameError;
            }
        }

        /// <summary>
        /// Loads the project files into memory and processes them
        /// </summary>
        public void ProcessProjectFiles()
        {
            while (_projectsToLoad.Count > 0)
            {
                // Pick up a project to process, from the stack
                ProjectInfo projectLoad = GetOrCreateProjectInfo(_projectsToLoad.Pop());
                if (!projectLoad.valid) continue;
                if (projectLoad.processed) continue;

                if (Verbosity >= Config.Verbosity.Verbose)
                    Console.WriteLine("Processing Project {0}", projectLoad.path);

                Nesting.AddProjectFile(projectLoad.path);

                try
                {
                    projectLoad.FindAllReferences(this, _config.skipRefs);
                    _actualProjects.Add(projectLoad);
                }
                catch (Exception e)
                {
                    // We want to continue loading the other projects even if one fails
                    SlnError.ReportError(SlnError.ErrorID.LoadFailed, projectLoad.path.fullName, e.Message);
                }
            }
        }

        /// <summary>
        /// Finds (or creates) project info for all projects
        /// </summary>
        public ProjectInfo GetOrCreateProjectInfo(string path)
        {
            VSPath vsPath = VSPath.GetFromFile(path);
            VSPath newVsPath = vsPath;
            if (vsPath.extension.ToLowerInvariant() == ".mproj")
            {
                newVsPath = VSPath.GetFromFile(Path.Combine(vsPath.directoryName, vsPath.projectName + "_generated.csproj"));
            }
            if (!_projectInfo.ContainsKey(newVsPath))
            {
                ProjectInfo project = MProjToCsProj.ConvertIfIsMProj(vsPath, newVsPath, Verbosity);

                _projectInfo[newVsPath] = project;

                if (_guidToProject.ContainsKey(project.guid))
                    SlnError.ReportError(
                        SlnError.ErrorID.DuplicateGuid,
                        newVsPath.ToString(),
                        new string[] { _guidToProject[project.guid].path.ToString(), project.guid }
                    );

                string name = project.path.projectName.ToLowerInvariant();
                if (_nameToProject.ContainsKey(name))
                    SlnError.ReportError(SlnError.ErrorID.DuplicateProjectName, newVsPath.ToString(), new string[] {
                            _nameToProject[name].path.ToString(),
                            "(VS cannot handle duplicate project names)",
                        });

                _guidToProject[project.guid] = project;
                _nameToProject[name] = project;
            }

            return _projectInfo[newVsPath];
        }

        /// <summary>
        /// Creates a VS Solution file listing all the projects that have been analyzed, in either
        /// VS 10 or VS 9 format.
        /// </summary>
        public void CreateTempSlnFile(string slnFile, bool nest, VisualStudioVersion targetVersion)
        {
            StringBuilder solutionItemsSection = new StringBuilder();
            if (_solutionItems.Count > 0)
            {
                //FIXME: DON'T replace the "tab" in strings with spaces. Otherwise, the setting doesn't work correctly in VS.
                solutionItemsSection.AppendFormat(@"
                    Project(""{0}"") = ""Solution Items"", ""Solution Items"", ""{1}""
	                    ProjectSection(SolutionItems) = preProject", NestingDir.typeGuid, NestingDir.solutionItemsGuid);

                foreach (var slnItem in _solutionItems)
                {
                    solutionItemsSection.AppendFormat(@"{0} = {0}", slnItem);
                }
                solutionItemsSection.AppendFormat(@"EndProjectSection
                    EndProject");
            }
            // Build the Project section string
            StringBuilder projectSection = new StringBuilder();
            StringBuilder projectHierarchy = new StringBuilder();
            StreamWriter writer;
            NestingDir root = Nesting.displayRoot;

            foreach (ProjectInfo project in _actualProjects)
            {
                string projectTypeGuid = null;
                switch (project.path.extension.ToLowerInvariant())
                {
                    case ".scopeproj": projectTypeGuid = _cosmosScopeProjectGuid; break;
                    case ".ccproj": projectTypeGuid = _cloudServiceProjectGuid; break;
                    case ".csproj":
                    // File extension for assembly used by bootstrap parser
                    case ".boot":
                        projectTypeGuid = _csharpProjectGuid;
                        break;
                    case ".dbproj":
                        // database projects
                        projectTypeGuid = _databaseProjectGuid; break;
                    case ".vbproj": projectTypeGuid = _vbProjectGuid; break;
                    case ".vcxproj": projectTypeGuid = _vcxProjectGuid; break;
                    // TODO once .qproj is registered as a package, use it's own guid
                    case ".qproj": projectTypeGuid = _csharpProjectGuid; break;
                    case ".xadproj": projectTypeGuid = _csharpProjectGuid; break;
                    case ".mproj":
                        throw new InvalidProgramException("mproj should have been converted to csproj");
                    case ".proj": projectTypeGuid = null; break; // Just ignore this
                    default:
                        if (Verbosity > Config.Verbosity.Quiet)
                            Console.WriteLine("Unknown project extension {0} encountered.", project.path.extension);
                        break;
                }

                if (projectTypeGuid != null)
                {
                    projectSection.AppendFormat(
                        "Project(\"{0}\") = \"{1}\", \"{2}\", \"{3}\"\r\nEndProject\r\n",
                        projectTypeGuid, project.path.projectName, project.path.fullName, project.guid
                    );

                    if (nest)
                    {
                        NestingDir dir = Nesting.GetDir(project.path);
                        if (dir.hasOnlySelfNamedProject)
                            dir = dir.parent;

                        if (dir != root)
                            projectHierarchy.AppendFormat(
                                "\t\t{0} = {1}\r\n",
                                project.guid,
                                dir.guid
                            );
                    }
                }

                StringBuilder dirSection = new StringBuilder();
                StringBuilder dirHierarchy = new StringBuilder();

                if (nest)
                {
                    foreach (NestingDir node in root.allChildren)
                    {
                        if (node.hasOnlySelfNamedProject)
                            continue;

                        dirSection.AppendFormat(
                            "Project(\"{0}\") = \"_{1}\", \"_{1}\", \"{2}\"\r\nEndProject\r\n",
                            NestingDir.typeGuid, node.path.vsName, node.guid
                        );

                        if (node.parent != root)
                        {
                            dirHierarchy.AppendFormat(
                                "\t\t{0} = {1}\r\n",
                                node.guid, node.parent.guid
                            );
                        }
                    }
                }

                string formatVer;
                string vsVer;
                switch (targetVersion)
                {
                    case VisualStudioVersion.VS10:
                        formatVer = "11.00";
                        vsVer = "10";
                        break;
                    case VisualStudioVersion.VS11:
                        formatVer = "12.0";
                        vsVer = "11";
                        break;
                    case VisualStudioVersion.VS12:
                        formatVer = "12.0";
                        vsVer = "12";
                        break;
                    case VisualStudioVersion.VS14:
                        formatVer = "14.0";
                        vsVer = "14";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("targetVersion");
                }

                writer = new StreamWriter(slnFile);
                writer.WriteLine("Microsoft Visual Studio Solution File, Format Version {0}", formatVer);
                writer.WriteLine("# Visual Studio {0}", vsVer);
                writer.WriteLine(solutionItemsSection.ToString());
                writer.Write(projectSection.ToString());

                if (nest)
                {
                    writer.Write(dirSection.ToString());

                    writer.WriteLine("Global");
                    writer.WriteLine("\tGlobalSection(NestedProjects) = preSolution");

                    writer.Write(projectHierarchy.ToString());
                    writer.Write(dirHierarchy.ToString());

                    writer.WriteLine("\tEndGlobalSection");
                    writer.WriteLine("EndGlobal");
                }

                writer.WriteLine();
                writer.Close();
            }

            //FIXME: DON'T replace the "tab" in strings with spaces. Otherwise, the setting doesn't work correctly in VS.
            StringBuilder globalSection = new StringBuilder();
            globalSection.Append(@"
Global");
            globalSection.AppendFormat(@"
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Mixed Platforms = Debug|Mixed Platforms
		Release|Mixed Platforms = Release|Mixed Platforms
	EndGlobalSection");
            globalSection.Append(@"
	GlobalSection(ProjectConfigurationPlatforms) = postSolution");

            foreach (ProjectInfo project in _actualProjects)
            {
                var ext = project.path.extension.ToLowerInvariant();

                if (ext == ".csproj")
                {
                    globalSection.AppendFormat(@"
		{0}.Debug|Mixed Platforms.ActiveCfg = Debug|Any CPU
		{0}.Debug|Mixed Platforms.Build.0 = Debug|Any CPU
	    {0}.Release|Mixed Platforms.ActiveCfg = Release|Any CPU
		{0}.Release|Mixed Platforms.Build.0 = Release|Any CPU", project.guid);
                }
                else if (ext == ".vcxproj")
                {
                    var is64 = project.path.fullName.EndsWith("64.vcxproj");
                    var platform = is64 ? "x64" : "Win32";
                    globalSection.AppendFormat(@"
		{0}.Debug|Mixed Platforms.ActiveCfg = Debug|{1}
		{0}.Debug|Mixed Platforms.Build.0 = Debug|{1}
		{0}.Release|Mixed Platforms.ActiveCfg = Debug|{1}
		{0}.Release|Mixed Platforms.Build.0 = Debug|{1}", project.guid, platform);
                }
            }

            globalSection.Append(@"
	EndGlobalSection");
            globalSection.Append(@"
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection");

            globalSection.Append(@"
EndGlobal");

            writer = new StreamWriter(slnFile, true);
            writer.WriteLine(globalSection.ToString());
            writer.Close();
        }

        /// <summary>
        /// Closure overlap checking
        /// </summary>
        public bool ValidateNoOverlap(string topLevel, ProjectClosure other, string otherTopLevel)
        {
            bool result = true;

            foreach (VSPath path in _projectInfo.Keys)
            {
                if (other._projectInfo.ContainsKey(path))
                {
                    SlnError.ReportError(
                        SlnError.ErrorID.ProjectsOverlapped,
                        path.ToString(),
                        new string[] { "Referenced in: ", topLevel, otherTopLevel }
                    );
                    result = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Get list of imports that are not marked in the registry as safe
        /// </summary>
        public ICollection<string> UnsafeImports
        {
            get
            {
                if (this.unsafeImports == null)
                {
                    HashSet<string> unsafeImportsSet = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                    HashSet<string> safeImports = GetSafeImports();

                    foreach (ProjectInfo projectInfo in _actualProjects)
                    {
                        foreach (ResolvedImport import in projectInfo.project.Imports)
                        {
                            if (!safeImports.Contains(import.ImportedProject.FullPath))
                            {
                                unsafeImportsSet.Add(import.ImportedProject.FullPath);
                            }
                        }
                    }
                    this.unsafeImports = unsafeImportsSet;
                }
                return this.unsafeImports;
            }
        }
        /// <summary>
        /// Get Hash Map of safe imports from the registry
        /// </summary>
        private HashSet<string> GetSafeImports()
        {
            var safeImports = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            string regPath = GetSafeImportsPath();

            RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath);

            if (key != null)
            {
                foreach (string valueName in key.GetValueNames())
                {
                    RegistryValueKind valueKind = key.GetValueKind(valueName);
                    if (valueKind == RegistryValueKind.String)
                    {
                        string value = (string)key.GetValue(valueName);
                        string expandedPath = Environment.ExpandEnvironmentVariables(value);
                        safeImports.Add(expandedPath);
                    }
                }
            }
            else
            {
                Console.Error.WriteLine("Safe imports registry key not found at " + regPath);
            }

            return safeImports;
        }

        private void CreateAddSafeImportsCmdScript(TextWriter script)
        {
            string regPath = GetSafeImportsPath();
            RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath, RegistryKeyPermissionCheck.ReadSubTree, System.Security.AccessControl.RegistryRights.ReadKey);

            if (key != null)
            {
                if (!Debugger.IsAttached)
                {
                    script.WriteLine("@ECHO OFF");
                }
                script.WriteLine("SET LOCAL");
                script.WriteLine("SET REG_EXE=%SYSTEMROOT%\\system32\\reg.exe");
                script.WriteLine();
                script.WriteLine("SET REG_KEY={0}", key.Name);
                script.WriteLine();
                script.WriteLine("ECHO Adding Safe Imports");
                int targetNumber = 0;
                foreach (string target in this.UnsafeImports)
                {
                    string valueName;
                    do
                    {
                        targetNumber++;
                        valueName = string.Format("SlnGen-{0}", targetNumber);
                    } while (key.GetValue(valueName) != null);

                    script.WriteLine("ECHO {0} = {1}", valueName, target);
                    script.WriteLine("%REG_EXE% ADD %REG_KEY% /v \"{0}\" /t REG_SZ /d \"{1}\"", valueName, target);
                    script.WriteLine();

                }
                script.WriteLine("END LOCAL");
            }
            else
            {
                Console.Error.WriteLine("Safe imports registry key not found at " + regPath);
                throw new InvalidOperationException("Safe imports registry key not found at " + regPath);
            }
        }

        public void AuthorizeUnsafeImports()
        {
            string tempScriptFilename = string.Format("{0}AddSafeImportsScript-{1}.cmd", Path.GetTempPath(), Guid.NewGuid().ToString("D"));
            using (TextWriter script = File.CreateText(tempScriptFilename))
            {
                this.CreateAddSafeImportsCmdScript(script);
            }

            try
            {
                var psi = new ProcessStartInfo()
                {
                    CreateNoWindow = true,
                    FileName = tempScriptFilename,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = false,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = Environment.CurrentDirectory,
                };

                if (!ProjectClosure.CanWriteSafeImports())
                {
                    psi.Verb = "runas";
                }

                Process p = Process.Start(psi);
                p.WaitForExit();
            }
            finally
            {
                try
                {
                    File.Delete(tempScriptFilename);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }

        public bool HasUnsafeImports
        {
            get
            {
                return this.UnsafeImports.Count > 0;
            }
        }

        public void FixToolVersions()
        {
            string desiredToolsVersion;
            switch (this._config.targetVersion)
            {
                case VisualStudioVersion.VS10:
                case VisualStudioVersion.VS11:
                case VisualStudioVersion.VS12:
                default:
                    desiredToolsVersion = "4.0";
                    break;
            }

            foreach (KeyValuePair<VSPath, ProjectInfo> kvp in _projectInfo)
            {
                string fileName = kvp.Key.fullName;
                Project project = kvp.Value.project;

                XElement element = XElement.Load(fileName, LoadOptions.PreserveWhitespace);
                string actualVersion = element.Attribute("ToolsVersion").Value;

                if (actualVersion != desiredToolsVersion)
                {
                    if ((File.GetAttributes(fileName) & FileAttributes.ReadOnly) != 0)
                    {
                        Process.Start("sd.exe", "edit " + fileName).WaitForExit();
                        if ((File.GetAttributes(fileName) & FileAttributes.ReadOnly) != 0)
                        {
                            Console.WriteLine("Warning: Could not edit file '{0}', not fixing tool version", fileName);
                            continue;
                        }
                    }

                    element.Attribute("ToolsVersion").Value = desiredToolsVersion;
                    element.Save(fileName);
                }
            }
        }

        private static string GetSafeImportsPath()
        {
            bool is64BitOS = CheckFor64BitOS();

            if (is64BitOS)
            {
                return @"SOFTWARE\Wow6432Node\Microsoft\VisualStudio\10.0\MSBuild\SafeImports";
            }
            else
            {
                return @"SOFTWARE\Microsoft\VisualStudio\10.0\MSBuild\SafeImports";
            }
        }

        private static bool CanWriteSafeImports()
        {
            string regPath = GetSafeImportsPath();
            try
            {
                RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath, RegistryKeyPermissionCheck.ReadSubTree, System.Security.AccessControl.RegistryRights.WriteKey);
            }
            catch (SecurityException)
            {
                //This is bad coding but checking permissions of ACLS is a lot of code.
                return false;
            }
            return true;
        }

        private static bool CheckFor64BitOS()
        {
            bool is64BitOS = false;
            if (System.Environment.OSVersion.Version.Major >= 5 && System.Environment.OSVersion.Version.Minor >= 1)
            {
                SYSTEM_INFO sysInfo = new SYSTEM_INFO();
                GetNativeSystemInfo(ref sysInfo);
                if (sysInfo.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_AMD64
                    || sysInfo.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_IA64)
                {
                    is64BitOS = true;
                }
            }
            return is64BitOS;
        }

        [DllImport("kernel32.dll")]
        internal static extern void GetNativeSystemInfo(ref SYSTEM_INFO lpSystemInfo);

        internal const ushort PROCESSOR_ARCHITECTURE_INTEL = 0;
        internal const ushort PROCESSOR_ARCHITECTURE_IA64 = 6;
        internal const ushort PROCESSOR_ARCHITECTURE_AMD64 = 9;
        internal const ushort PROCESSOR_ARCHITECTURE_UNKNOWN = 0xFFFF;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEM_INFO
        {
            public ushort wProcessorArchitecture;
            public ushort wReserved;
            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public UIntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        };
    }
}
