using System;
using System.Collections.Generic;

namespace SlnGen
{
    public class NestingDir
    {
        public static string typeGuid { get { return "{2150E333-8FDC-42A3-9474-1A3956D46DE8}"; } }
        public static string solutionItemsGuid { get { return "{DBD159B4-D86B-4268-97A0-0C2B44FDB374}"; } }

        public Guid realGuid { get; internal set; }
        public string guid { get { return realGuid.ToString("B"); } }
        public VSPath path { get; internal set; }

        public NestingDir parent { get; internal set; }
        public SortedList<string, NestingDir> children { get; internal set; }
        public List<NestingDir> allChildren
        {
            get
            {
                List<NestingDir> result = new List<NestingDir>();
                foreach (NestingDir child in children.Values)
                {
                    result.Add(child);
                    result.AddRange(child.allChildren);
                }

                return result;
            }
        }
        public int countAllUnder
        {
            get
            {
                int result = 1;
                foreach (NestingDir child in children.Values)
                    result += child.countAllUnder;
                return result;
            }
        }

        public SortedList<string, string> projects { get; internal set; }
        public bool hasOnlySelfNamedProject
        {
            get
            {
                if (children.Count != 0) return false;
                if (projects.Count != 1) return false;
                if (!projects.ContainsKey(path.vsName.ToLowerInvariant())) return false;
                return true;
            }
        }

        public bool isDisplayRoot { get { return this == displayRoot; } }
        public NestingDir realRoot { get; internal set; }
        public NestingDir displayRoot
        {
            get
            {
                NestingDir result = realRoot;

                while ((result.children.Count == 1) && (result.projects.Count == 0))
                    result = result.children.Values[0];

                return result;
            }
        }

        // Create the real root
        public NestingDir()
        {
            realGuid = Guid.NewGuid();
            path = VSPath.emptyPath;

            parent = null;
            children = new SortedList<string, NestingDir>();

            realRoot = this;

            projects = new SortedList<string, string>();
        }

        private NestingDir(NestingDir theParent, VSPath thePath)
        {
            path = thePath;
            realGuid = Guid.NewGuid();

            parent = theParent;
            children = new SortedList<string, NestingDir>();

            realRoot = parent.realRoot;

            projects = new SortedList<string, string>();
        }

        public NestingDir GetDir(VSPath path)
        {
            NestingDir current = realRoot;

            for (int i = path.vsPathComponents.Count - 1; i >= 0; i--)
            {
                VSPath currentPath = path.GetAncestorDirectory(i);
                string name = currentPath.vsName.ToLowerInvariant();

                if (!current.children.ContainsKey(name))
                    current.children.Add(name, new NestingDir(current, currentPath));

                current = current.children[name];
            }

            return current;
        }

        public void AddProjectFile(VSPath path)
        {
            NestingDir node = GetDir(path);
            node.projects[path.projectName.ToLowerInvariant()] = path.projectName;
        }
    }
}
