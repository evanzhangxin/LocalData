using System;
using System.Collections.Generic;

namespace SlnGen
{
    public class SlnError
    {
        public enum ErrorID
        {
            NoErrors,
            DefaultErrors,
            AllErrors,
            PedanticErrors,

            MissingFile,
            LoadFailed,
            MissingOrBadGuid,
            MissingVersion,
            BadVersion,
            MissingAssemblyName,
            MissingRelativeOutputPath,
            MissingPreBuildImport,
            MissingPostBuildImport,
            MismatchedAssemblyName,
            DuplicateGuid,
            DuplicateProjectName,
            ProjectsOverlapped,
        };

        private static ErrorID[] _defaultEnabled = new ErrorID[] {
            ErrorID.MissingFile,
            ErrorID.LoadFailed,
            ErrorID.MissingOrBadGuid,
//            ErrorID.MissingVersion,
            ErrorID.BadVersion,
            ErrorID.MissingAssemblyName,
//            ErrorID.MissingRelativeOutputPath,
//            ErrorID.MissingPreBuildImport,
//            ErrorID.MissingPostBuildImport,
//            ErrorID.MismatchedAssemblyName,
            ErrorID.DuplicateGuid,
            ErrorID.DuplicateProjectName,
//            ErrorID.ProjectsOverlapped,
        };
        public static ErrorID[] defaultEnabled { get { return _defaultEnabled; } }

        private static ErrorID[] _allEnabled = new ErrorID[] {
            ErrorID.MissingFile,
            ErrorID.LoadFailed,
            ErrorID.MissingOrBadGuid,
            ErrorID.MissingVersion,
            ErrorID.BadVersion,
            ErrorID.MissingAssemblyName,
//            ErrorID.MissingRelativeOutputPath,
//            ErrorID.MissingPreBuildImport,
//            ErrorID.MissingPostBuildImport,
            ErrorID.MismatchedAssemblyName,
            ErrorID.DuplicateGuid,
            ErrorID.DuplicateProjectName,
            ErrorID.ProjectsOverlapped,
        };
        public static ErrorID[] allEnabled { get { return _allEnabled; } }

        private static ErrorID[] _pedantic = new ErrorID[] {
            ErrorID.MissingFile,
            ErrorID.LoadFailed,
            ErrorID.MissingOrBadGuid,
            ErrorID.MissingVersion,
            ErrorID.BadVersion,
            ErrorID.MissingAssemblyName,
            ErrorID.MissingRelativeOutputPath,
            ErrorID.MissingPreBuildImport,
            ErrorID.MissingPostBuildImport,
            ErrorID.MismatchedAssemblyName,
            ErrorID.DuplicateGuid,
            ErrorID.DuplicateProjectName,
            ErrorID.ProjectsOverlapped,
        };

        public static Dictionary<ErrorID, ErrorID> enabled { get; internal set; }
        public static List<string> names { get; internal set; }
        public static Dictionary<string, ErrorID> translate { get; internal set; }
        public static List<SlnError> errors { get; internal set; }

        public ErrorID id { get; internal set; }
        public string source { get; internal set; }
        public string[] extraInfo { get; internal set; }

        public SlnError(ErrorID theID, string theSource, string[] theExtraInfo)
        {
            id = theID;
            source = theSource;
            extraInfo = theExtraInfo;
        }

        public static void Init()
        {
            enabled = new Dictionary<ErrorID, ErrorID>();
            foreach (ErrorID id in _defaultEnabled)
                enabled[id] = id;

            names = new List<string>();
            foreach (ErrorID id in _allEnabled)
                names.Add(String.Format("{0}", id));

            translate = new Dictionary<string, ErrorID>();
            foreach (ErrorID id in _pedantic)
            {
                string name = String.Format("{0}", id).ToLowerInvariant();
                translate[name] = id;
                translate["-" + name] = id;
            }

            translate["none"] = ErrorID.NoErrors;
            translate["default"] = ErrorID.DefaultErrors;
            translate["all"] = ErrorID.AllErrors;
            translate["pedantic"] = ErrorID.PedanticErrors;

            errors = new List<SlnError>();
        }

        public static bool ChangeEnabled(string theName)
        {
            string name = theName.ToLowerInvariant();
            if (!translate.ContainsKey(name))
                return false;

            ErrorID id = translate[name];
            bool enable = !name.StartsWith("-");

            switch (id)
            {
                case ErrorID.NoErrors:
                    enabled.Clear();
                    break;

                case ErrorID.DefaultErrors:
                    enabled.Clear();
                    foreach (ErrorID toEnable in _defaultEnabled)
                        enabled[toEnable] = toEnable;
                    break;

                case ErrorID.AllErrors:
                    enabled.Clear();
                    foreach (ErrorID toEnable in _allEnabled)
                        enabled[toEnable] = toEnable;
                    break;

                case ErrorID.PedanticErrors:
                    enabled.Clear();
                    foreach (ErrorID toEnable in _pedantic)
                        enabled[toEnable] = toEnable;
                    break;

                default:
                    if (enable)
                        enabled[id] = id;
                    else
                        enabled.Remove(id);
                    break;
            }

            return true;
        }

        public static void ReportError(ErrorID theID, string theSource)
        {
            if (!enabled.ContainsKey(theID))
                return;

            errors.Add(new SlnError(theID, theSource, null));
        }

        public static void ReportError(ErrorID theID, string theSource, string theExtraInfo)
        {
            if (!enabled.ContainsKey(theID))
                return;

            errors.Add(new SlnError(theID, theSource, new string[] { theExtraInfo }));
        }

        public static void ReportError(ErrorID theID, string theSource, string[] theExtraInfo)
        {
            if (!enabled.ContainsKey(theID))
                return;

            errors.Add(new SlnError(theID, theSource, theExtraInfo));
        }

        public static void PrintErrors(Config.Verbosity verbosity)
        {
            if (errors.Count == 0)
                return;

            Console.WriteLine("{0} errors were found.", errors.Count);

            if (verbosity < Config.Verbosity.Normal)
                return;

            foreach (SlnError error in errors)
            {
                Console.WriteLine("Error {0} in {1}", error.id, error.source);

                if (error.extraInfo != null)
                {
                    foreach (string extra in error.extraInfo)
                        Console.WriteLine("\t{0}", extra);
                }
            }
        }
    }
}
