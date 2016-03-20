using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;


namespace SlnGen
{
    public class ProjectInfo
    {
        public enum WarningID
        {
            MissingFile,
            LoadFailed,
            MissingGuid,
            MissingVersion,
            BadVersion,
            MissingAssemblyName,
            MissingRelativeOutputPath,
            MissingPreBuildImport,
            MissingPostBuildImport,
            MismatchedAssemblyName,
            DuplicateGuids,
            DuplicateProjects,
        };

        private static readonly Guid _badGuid = new Guid("{9501A45E-ACCA-4B96-84E0-8A12E20D88CA}");

        private Project _project = null;
        private bool _processed = false;

        private bool _valid = false;
        private List<WarningID> _warnings = new List<WarningID>();

        private Guid _guid;

        private bool _compilable = false;

        private Config.Verbosity _verbosity = Config.Verbosity.Normal;

        public Project project { get { return _project; } }
        public bool processed { get { return _processed; } }

        public bool valid { get { return _valid; } }
        public List<WarningID> warnings { get { return _warnings; } }

        public string lowerpath { get { return path.fullName.ToLowerInvariant(); } }
        public VSPath path { get; internal set; }

        public string guid { get { return _guid.ToString("B").ToUpperInvariant(); } }
        public string version { get { return project.GetPropertyValue("ProductVersion"); } }
        public string assemblyName { get { return project.GetPropertyValue("AssemblyName"); } }
        public bool compilable { get { return _compilable; } }

        public ProjectInfo(string pathname, Config.Verbosity verbosity)
        {
            path = VSPath.GetFromFile(pathname);
            _compilable = false;
            _verbosity = verbosity;

            if (!File.Exists(path.fullName))
            {
                SlnError.ReportError(SlnError.ErrorID.MissingFile, path.ToString());
                return;
            }


            try
            {
                _project = new Project(path.fullName);
            }
            catch (InvalidProjectFileException e)
            {
                SlnError.ReportError(SlnError.ErrorID.LoadFailed, path.ToString(), e.BaseMessage);
                _project = null;
                return;
            }

            _valid = true;

            // Check to see if this project has any Compile or MCompile build items.
            _compilable = _project.Items.Any(
                (item) =>
                {
                    var itemType = item.ItemType.ToUpperInvariant();
                    return itemType == "COMPILE" || itemType == "CLCOMPILE" || itemType == "MCOMPILE";
                });

            // If a project guid is already defined, use it or else create a new one
            try
            {
                _guid = new Guid(project.GetPropertyValue("ProjectGuid"));
            }
            catch (Exception)
            {
                _guid = Guid.NewGuid();

                if (_compilable)
                {
                    string guid = project.GetPropertyValue("ProjectGuid");
                    string[] message;
                    if (guid == null)
                        message = new string[] { "Missing Guid" };
                    else
                        message = new string[] { "Malformed Guid", guid };

                    SlnError.ReportError(SlnError.ErrorID.MissingOrBadGuid, path.ToString(), message);
                }
            }

            if ((_guid == _badGuid) && (_compilable))
                SlnError.ReportError(SlnError.ErrorID.MissingOrBadGuid, path.ToString(), new string[] { "Bad GUID", _badGuid.ToString("B") });

            // Check for missing/bad msbuild version
            if (!_compilable)
            {
                // Skip version check if the file is not compilable.
            }
            else if (string.IsNullOrEmpty(version))
            {
                SlnError.ReportError(SlnError.ErrorID.MissingVersion, path.ToString());
            }
            else if (!version.StartsWith("10."))
            {
                SlnError.ReportError(SlnError.ErrorID.BadVersion, path.ToString(), version);
                _valid = false;
            }

            if (_compilable && (assemblyName == null))
                SlnError.ReportError(SlnError.ErrorID.MissingAssemblyName, path.ToString());

            // Does the project name match the assembly name?
            if ((!string.IsNullOrEmpty(assemblyName)) &&
                (assemblyName.ToLowerInvariant() != path.projectName.ToLowerInvariant()))
            {
                SlnError.ReportError(SlnError.ErrorID.MismatchedAssemblyName, path.ToString(), String.Format("Assembly name is: {0}", assemblyName));
            }
        }

        /// <summary>
        /// Finds all references to other projects from this one.
        /// </summary>
        public void FindAllReferences(ProjectClosure closure, bool skipRefs)
        {
            _processed = true;

            var projectReferences = new List<ProjectItem>();
            projectReferences.AddRange(_project.GetItems("ProjectReference"));
            projectReferences.AddRange(_project.GetItems("ProjectFile"));
            projectReferences.AddRange(_project.GetItems("SolutionItem"));

            foreach (ProjectItem projectReference in projectReferences)
            {
                if (projectReference.ItemType == "SolutionItem")
                {
                    var slnItem = Path.Combine(path.directoryName, projectReference.UnevaluatedInclude);
                    slnItem = Path.GetFullPath(slnItem);
                    System.Uri uri1 = new Uri(slnItem);
                    System.Uri uri2 = new Uri(closure.Config.slnFile);
                    Uri relativeUri = uri2.MakeRelativeUri(uri1);
                    var slnItemPath = relativeUri.ToString().Replace("file:///", "").Replace(@"/", @"\");
                    closure.SolutionItems.Add(slnItemPath);
                    continue;
                }

                if (skipRefs &&
                    string.Equals(projectReference.GetMetadataValue("ReferenceOutputAssembly"), "False", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                string referenceProject;
                if (Path.IsPathRooted(projectReference.UnevaluatedInclude))
                    referenceProject = Path.GetFullPath(projectReference.UnevaluatedInclude);
                else
                {
                    referenceProject = Path.Combine(path.directoryName, projectReference.UnevaluatedInclude);
                    referenceProject = Path.GetFullPath(referenceProject);
                }
                
                if (!string.IsNullOrEmpty(referenceProject) && File.Exists(referenceProject))
                {
                    closure.AddEntriesToParseFiles(referenceProject, false);
                }
            }
        }
    }
}
