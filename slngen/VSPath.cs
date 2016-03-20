namespace SlnGen
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class VSPath
    {
        static private char[] _directoryChars = new char[] { Path.DirectorySeparatorChar };

        static public string drivePrefix { get { return "drive_"; } }
        static public string networkPrefix { get { return "network_"; } }
        static public string directorySeperator { get { return Path.DirectorySeparatorChar.ToString(); } }
        static public string volumeSeperator { get { return Path.VolumeSeparatorChar.ToString(); } }

        static private VSPath _emptyPath = new VSPath("", "", "");
        static public VSPath emptyPath { get { return _emptyPath; } }

        public string fullName { get; internal set; }
        public string lowerFullName { get { return fullName.ToLowerInvariant(); } }
        public string directoryName { get; internal set; }
        public string projectName { get; internal set; }
        public string extension { get; internal set; }

        public string vsPath { get; internal set; }
        public List<string> vsPathComponents { get; internal set; }
        public string vsName
        {
            get
            {
                if (vsPathComponents.Count == 0) return "";
                return vsPathComponents[vsPathComponents.Count - 1];
            }
        }

        private VSPath(string theDirectory, string theProject, string theExtension)
        {
            directoryName = theDirectory;
            projectName = theProject;
            extension = theExtension;
            vsPath = "";
            vsPathComponents = new List<string>();

            fullName = Path.Combine(theDirectory, theProject + theExtension);

            if (string.IsNullOrEmpty(directoryName))
                return;

            vsPathComponents.AddRange(directoryName.Split(_directoryChars, StringSplitOptions.RemoveEmptyEntries));
            string volume = vsPathComponents[0];

            if (volume.EndsWith(volumeSeperator))
                vsPathComponents[0] = drivePrefix + volume.Substring(0, volume.Length - 1);
            else
                vsPathComponents[0] = networkPrefix + volume;

            vsPathComponents = vsPathComponents;
            vsPath = string.Join(directorySeperator, vsPathComponents.ToArray());
        }

        private VSPath(IEnumerable<string> theVSCompatibleComponents, int levelsDown)
        {
            directoryName = "";
            vsPathComponents = new List<string>(theVSCompatibleComponents);
            vsPath = string.Join(directorySeperator, vsPathComponents.ToArray());

            if (levelsDown > 0) 
                vsPathComponents.RemoveRange(vsPathComponents.Count - levelsDown, levelsDown);

            List<string> fileComponents = new List<string>(vsPathComponents);

            if (fileComponents[0].StartsWith(drivePrefix))
            {
                fileComponents[0] = fileComponents[0].Substring(drivePrefix.Length) + Path.VolumeSeparatorChar;

                if (fileComponents.Count == 1)
                    fileComponents.Add("");
            }

            if (fileComponents[0].StartsWith(networkPrefix))
            {
                fileComponents[0] = fileComponents[0].Substring(networkPrefix.Length);

                if (fileComponents.Count == 1)
                    fileComponents.Add("");

                fileComponents.Insert(0, "");
                fileComponents.Insert(0, "");
            }

            directoryName = string.Join(directorySeperator, fileComponents.ToArray());
            fullName = directoryName;
        }

        static public VSPath GetFromDir(string theDirPath)
        {
            string path = "";
            try { path = Path.GetFullPath(theDirPath); }
            catch { }

            if (string.IsNullOrEmpty(path))
                return emptyPath;

            return new VSPath(path, "", "");
        }

        static public VSPath GetFromFile(string theFilePath)
        {
            string fullPath = "";
            string filePath = "";
            string dirPath = "";
            string extentionPath = "";
            try
            {
                fullPath = Path.GetFullPath(theFilePath);
                filePath = Path.GetFileNameWithoutExtension(fullPath);
                dirPath = Path.GetDirectoryName(fullPath) ?? "";
                extentionPath = Path.GetExtension(fullPath);
            }
            catch { }

            if (string.IsNullOrEmpty(fullPath))
                return emptyPath;

            return new VSPath(dirPath, filePath, extentionPath);
        }

        static public VSPath GetFromComponents(IEnumerable<string> components)
        {
            if (components == null)
                return emptyPath;

            if (!components.GetEnumerator().MoveNext())
                return emptyPath;

            return new VSPath(components, 0);
        }

        public VSPath GetAncestorDirectory(int levelsDown)
        {
            if ((vsPathComponents.Count == 0) || (levelsDown >= vsPathComponents.Count))
                return emptyPath;

            if (levelsDown <= 0)
                levelsDown = 0;

            return new VSPath(vsPathComponents, levelsDown);
        }

        public override bool Equals(object o)
        {
            if (this.GetType() != o.GetType())
                return false;

            VSPath other = (VSPath)o;

            return fullName.ToLowerInvariant() == other.fullName.ToLowerInvariant() &&
                vsPath.ToLowerInvariant() == other.vsPath.ToLowerInvariant();
        }

        public override string ToString()
        {
            return this.fullName.ToLowerInvariant();
        }

        public override int GetHashCode()
        {
            return fullName.ToLowerInvariant().GetHashCode();
        }
    }
}
