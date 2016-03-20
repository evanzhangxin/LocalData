//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace SlnGen
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;

    public interface IProgramArgumentConverter
    {
        object Convert(object value, Type targetType);
        object ConvertBack(object value, Type targetType);
    }

    public class ProgramArgumentInfo
    {
        private object defaultValue;

        public ProgramArgumentInfo()
        {
            ShortForms = new List<string>();
            Type = typeof(bool);
        }

        public string Name { get; set; }
        public IList<string> ShortForms { get; internal set; }
        public Type Type { get; set; }
        public object Default { get { return defaultValue; } set { defaultValue = value; HasDefault = true; } }
        public bool HasDefault { get; internal set; }
        public string Description { get; set; }
        public IProgramArgumentConverter Converter { get; set; }
        public bool IsPositional { get; set; }
        public string ValueName { get; set; }
    }

    public class TypeBasedConverter : IProgramArgumentConverter
    {
        public object Convert(object value, Type targetType)
        {
            if (value != null && targetType.IsAssignableFrom(value.GetType())) { return value; }
            return TypeDescriptor.GetConverter(targetType).ConvertFrom(value);
        }

        public object ConvertBack(object value, Type targetType)
        {
            if (value == null) { return null; }
            if (targetType.IsAssignableFrom(value.GetType())) { return value; }
            return TypeDescriptor.GetConverter(value.GetType()).ConvertTo(value, targetType);
        }
    }

    public class ProgramArguments
    {
        private static IProgramArgumentConverter DefaultConverter = new TypeBasedConverter();
        private static char[] ReservedArgNameCharacters = new char[] { '-', '+', ':', '=', '/' };
        private static ProgramArgumentInfo[] StandardInfos = {
            new ProgramArgumentInfo() {
                Name = "help",
                ShortForms = { "?", "h" },
                Description = ProgramArgumentsStrings.HelpArgumentDescription,
            },
        };

        private Dictionary<string, object> values = new Dictionary<string, object>();
        private ProgramArgumentInfo[] infos;
        private Exception parseException;

        public ProgramArguments(string cmdLine, ProgramArgumentInfo[] infos) :
            this(SplitArguments(cmdLine).Skip(1).ToArray(), infos)
        {
        }

        public ProgramArguments(string[] args, ProgramArgumentInfo[] infos)
        {
            // Check args
            if (args == null) { throw new ArgumentNullException("args"); }
            if (infos == null) { throw new ArgumentNullException("infos"); }

            foreach (ProgramArgumentInfo info in infos)
            {
                foreach (char reserved in ReservedArgNameCharacters)
                {
                    if (info.Name.Contains(reserved))
                    {
                        throw new ArgumentException(
                            string.Format(ProgramArgumentsStrings.ReservedCharacterInArgument, reserved, info.Name)
                        );
                    }
                }
            }

            // Process args
            this.infos = infos;
            ProcessAllArguments(args);
        }

        public Exception UsageException
        {
            get { return parseException; }
            set { parseException = value; ShouldShowUsageAndExit = true; }
        }

        public bool ShouldShowUsageAndExit
        {
            get;
            internal set;
        }

        private class StringChunk
        {
            int width;
            List<StringBuilder> lines = new List<StringBuilder>();
            string spaces;

            public StringChunk(int outdent, int width)
            {
                this.width = width;
                spaces = new string(' ', outdent);
                lines.Add(new StringBuilder());
            }

            public void Append(object value)
            {
                StringBuilder sb = lines[lines.Count - 1];
                sb.Append(value);
                if (sb.Length > width)
                {
                    bool foundNonSpace = false;
                    for (int i = Math.Min(sb.Length - 1, width); i >= 0; --i)
                    {
                        if (sb[i] == ' ' && foundNonSpace)
                        {
                            int wrapCount = sb.Length - i - 1;
                            var sbNext = new StringBuilder(spaces);
                            sbNext.Append(sb.ToString(), i + 1, wrapCount);
                            sb.Remove(i, wrapCount + 1);
                            lines.Add(sbNext);
                            break;
                        }
                        else
                        {
                            foundNonSpace = true;
                        }
                    }
                }
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                foreach (StringBuilder line in lines)
                {
                    if (sb.Length != 0) { sb.AppendLine(); }
                    sb.Append(line.ToString());
                }

                return sb.ToString();
            }

        }

        public string Logo
        {
            get
            {
                var sb = new StringBuilder();
                Assembly assem = Assembly.GetEntryAssembly();
                var titleAttrib = assem.GetCustomAttributes(true).OfType<AssemblyTitleAttribute>().FirstOrDefault();
                var copyrightAttrib = assem.GetCustomAttributes(true).OfType<AssemblyCopyrightAttribute>().FirstOrDefault();
                var descrAttrib = assem.GetCustomAttributes(true).OfType<AssemblyDescriptionAttribute>().FirstOrDefault();

                if (titleAttrib != null && !string.IsNullOrEmpty(titleAttrib.Title)) { sb.Append(titleAttrib.Title); }

                if (sb.Length != 0) { sb.Append(' '); }
                sb.Append("v" + Assembly.GetEntryAssembly().GetName().Version.ToString());
                
                if (sb.Length != 0) { sb.AppendLine(); }
                if (copyrightAttrib != null && !string.IsNullOrEmpty(copyrightAttrib.Copyright))
                {
                    sb.AppendLine(copyrightAttrib.Copyright.Replace("©", "(c)"));
                }
                if (descrAttrib != null && !string.IsNullOrEmpty(descrAttrib.Description))
                {
                    sb.AppendLine(descrAttrib.Description);
                }
                return sb.ToString();
            }
        }

        public string Usage
        {
            get
            {
                // TODO, csells, categories ala csc.exe

                var sb = new StringBuilder();
                sb.Append(Logo);

                if (parseException != null)
                {
                    sb.AppendLine();
                    sb.AppendLine(parseException.Message);
                }

                string exe = Path.GetFileName(SplitArguments(Environment.CommandLine).First());
                var shortForms = new StringChunk(7, 80);
                shortForms.Append(ProgramArgumentsStrings.Usage + exe + " [@argfile] [/help|h|?]");

                // short-form args
                foreach (ProgramArgumentInfo info in infos)
                {
                    shortForms.Append(' ');
                    if (info.HasDefault) { shortForms.Append('['); }
                    if (!info.IsPositional) { shortForms.Append('/'); }
                    shortForms.Append(info.Name);
                    foreach (var sf in info.ShortForms) { shortForms.Append("|" + sf); }

                    if (!info.IsPositional)
                    {
                        shortForms.Append(GetValueUsageSnippet(info));
                    }

                    if (info.HasDefault) { shortForms.Append(']'); }
                }

                sb.AppendLine();
                sb.Append(shortForms.ToString());

                // TODO, csells, determine outdent based on longest arg name (to a reasonable max)
                var longForms = new List<StringChunk>();
                for (int i = 0; i != infos.Length + 2; ++i) { longForms.Add(new StringChunk(22, 80)); }
                longForms[0].Append("@argfile              " + ProgramArgumentsStrings.ArgFileArgumentDescription);
                longForms[1].Append("/help                 " + 
                    ProgramArgumentsStrings.HelpArgumentDescription + 
                    " " +
                    ProgramArgumentsStrings.ShortForms +
                    " /h, /?.");

                // long-form args
                int longFormIndex = 2;
                foreach (ProgramArgumentInfo info in infos)
                {
                    StringChunk line = longForms[longFormIndex];

                    if (!info.IsPositional) { line.Append('/'); }
                    line.Append(info.Name);
                    if (!info.IsPositional)
                    {
                        line.Append(GetValueUsageSnippet(info));
                    }

                    int lineLength = line.ToString().Length;
                    if (lineLength < 22)
                    {
                        line.Append(new string(' ', 22 - lineLength));
                    }
                    else
                    {
                        line.Append(' ');
                    }

                    if (!string.IsNullOrEmpty(info.Description)) { line.Append(info.Description + " "); }
                    if (info.HasDefault && info.Default != null && !info.Type.HasElementType)
                    {
                        IProgramArgumentConverter converter = GetConverter(info);
                        line.Append(ProgramArgumentsStrings.Default + " "
                            + (string)converter.ConvertBack(info.Default, typeof(string)) + ". ");
                    }

                    if (info.ShortForms.Count != 0)
                    {
                        int count = info.ShortForms.Count;
                        line.Append(
                            count == 1 ? ProgramArgumentsStrings.ShortForm : ProgramArgumentsStrings.ShortForms
                        );
                        for (int i = 0; i != count; ++i)
                        {
                            if (i != 0) { line.Append(", "); }
                            line.Append("/" + info.ShortForms[i]);
                        }
                        line.Append(".");
                    }

                    ++longFormIndex;
                }

                sb.AppendLine();
                sb.AppendLine();
                foreach (StringChunk longForm in longForms) { sb.AppendLine(longForm.ToString()); }

                return sb.ToString();
            }
        }

        public static IEnumerable<string> ExpandWildCardFiles(IEnumerable<string> files, bool recurse)
        {
            foreach (string wild in files)
            {
                foreach (string file in ExpandWildCardFile(wild, recurse))
                {
                    yield return file;
                }
            }
        }

        public static IEnumerable<string> ExpandWildCardFile(string file, bool recurse)
        {
            bool hasWildcard = file.IndexOfAny(new char[] { '*', '?' }) != -1;

            if (!hasWildcard)
            {
                // If we're just given just a folder, don't match anything
                if (Directory.Exists(file)) { return new string[0]; }

                // If we're given a file w/o any wildcards and it doesn't exist, throw
                if (!File.Exists(file))
                {
                    throw new FileNotFoundException(string.Format(ProgramArgumentsStrings.FileNotFound, file));
                }
            }

            string pattern = Path.GetFileName(file);
            string dir = file.Substring(0, file.Length - pattern.Length);
            dir = string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : Path.GetFullPath(dir);
            return Directory.GetFiles(dir, pattern, recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }

        string GetValueUsageSnippet(ProgramArgumentInfo info)
        {
            var sb = new StringBuilder();
            if (typeof(Enum).IsAssignableFrom(info.Type))
            {
                IProgramArgumentConverter converter = GetConverter(info);
                foreach (object value in Enum.GetValues(info.Type))
                {
                    if (sb.Length == 0) { sb.Append(":("); } else { sb.Append('|'); }
                    sb.Append(converter.ConvertBack(value, typeof(string)));
                }
                sb.Append(')');
            }
            else if (!info.IsPositional && (info.Type == typeof(bool) || info.Type == typeof(bool[])))
            {
                sb.Append("[+|-]");
            }
            else
            {
                // TODO, csells, named value names, e.g. /file:inputFile
                sb.Append(":" + (string.IsNullOrEmpty(info.ValueName) ? "value" : info.ValueName));
            }

            return sb.ToString();
        }

        public int ExitCode { get { return !ShouldShowUsageAndExit || UsageException == null ? 0 : -1; } }

        public T GetValue<T>(string name)
        {
            if (values.ContainsKey(name))
            {
                object value = values[name];
                if (typeof(IEnumerable).IsAssignableFrom(typeof(T)) && (typeof(T) != typeof(string)))
                {
                    if (typeof(Array).IsAssignableFrom(typeof(T)))
                    {
                        var arrayList = (ArrayList)value;
                        return (T)(object)arrayList.ToArray(arrayList[0].GetType());
                    }
                    else
                    {
                        throw new Exception(ProgramArgumentsStrings.ArrayCollectionsSupported);
                    }
                }
                else
                {
                    return (T)value;
                }
            }

            ProgramArgumentInfo info = infos.First(i => string.Compare(i.Name, name, true) == 0);
            if (info.HasDefault) { return (T)info.Default; }

            throw new Exception(string.Format(ProgramArgumentsStrings.RequiredArgumentNotSet, name));
        }

        public void SetValue(string name, object value)
        {
            values[name] = value;
        }

        IProgramArgumentConverter GetConverter(ProgramArgumentInfo info)
        {
            return info.Converter ?? DefaultConverter;
        }

        void ProcessAllArguments(string[] args)
        {
            try
            {
                // Process arguments given
                ProcessArguments(args);

                // Check that all required args have been found
                foreach (ProgramArgumentInfo info in infos)
                {
                    if (!info.HasDefault && !values.ContainsKey(info.Name))
                    {
                        throw new Exception(string.Format(ProgramArgumentsStrings.RequiredArgumentNotSet, info.Name));
                    }
                }

                // Check for help
                if (values.ContainsKey("help") && (bool)values["help"]) { ShouldShowUsageAndExit = true; }
            }
            catch (Exception ex)
            {
                UsageException = ex;
            }
        }

        void ProcessArguments(IEnumerable<string> argsEnum)
        {
            string[] args = SplitArgumentNames(argsEnum).ToArray();

            int i = 0;
            while (i != args.Length)
            {
                bool nextArgUsed;
                ProcessSingleArgument(args[i], i + 1 == args.Length ? null : args[i + 1], out nextArgUsed);
                if (nextArgUsed) { ++i; }
                ++i;
            }
        }

        void ProcessArgumentFile(string argFilePath)
        {
            foreach (string line in File.ReadAllLines(argFilePath))
            {
                // strip off # comments
                int poundOffset = line.IndexOf('#');
                string argLine = poundOffset == -1 ? line : line.Substring(0, poundOffset);
                ProcessArguments(SplitArguments(argLine));
            }
        }

        // This method assumes quote handling and whitespace stripping has been done already by the shell or
        // SplitArguments, so don't call it directly
        void ProcessSingleArgument(string arg, string nextArg, out bool nextArgUsed)
        {
            // Assume we won't use the next arg as the value for this one
            nextArgUsed = false;

            if (arg[0] == '@')
            {
                // Strip off leading @
                ProcessArgumentFile(arg.Substring(1, arg.Length - 1));
                return;
            }

            ProgramArgumentInfo info = null;
            object boolOrStringValue = null;
            string larg = arg.ToLower();

            if (IsSwitch(larg))
            {
                // Switch based arg, e.g. /foo or -bar
                larg = larg.Substring(1);

                // Check for explicit +/- on a bool flag, e.g. /foo+ or /bar-
                bool? explicitBoolValue = null;
                if (larg.EndsWith("+") || larg.EndsWith("-"))
                {
                    explicitBoolValue = larg.EndsWith("+");
                    larg = larg.Substring(0, larg.Length - 1);
                }

                info = GetInfos().FirstOrDefault(delegate(ProgramArgumentInfo i)
                {
                    if (i.IsPositional) { return false; }
                    if (string.Compare(i.Name, larg, true) == 0) { return true; }
                    if (i.ShortForms.Any(sf => string.Compare(sf, larg, true) == 0)) { return true; }
                    return false;
                });

                if (info == null) { throw new Exception(string.Format(ProgramArgumentsStrings.UnknownArgument, arg)); }

                if (values.ContainsKey(info.Name) && !(values[info.Name] is IEnumerable))
                {
                    throw new Exception(string.Format(ProgramArgumentsStrings.CannotSetArgumentMultipleTimes, arg));
                }

                // At this point, we only have a string or a bool
                if (!info.IsPositional && (info.Type != typeof(bool) && info.Type != typeof(bool[])))
                {
                    // value'd argument
                    if (nextArg == null)
                    {
                        throw new Exception(string.Format(ProgramArgumentsStrings.MissingSwitchValue, arg));
                    }
                    nextArgUsed = true;
                    boolOrStringValue = nextArg;
                }
                else
                {
                    // just a flag
                    boolOrStringValue = explicitBoolValue ?? true;
                }
            }
            else
            {
                // Positional arg
                info = GetInfos().FirstOrDefault(delegate(ProgramArgumentInfo i)
                {
                    if (!i.IsPositional) { return false; }
                    if (!values.ContainsKey(i.Name)) { return true; }
                    return values[i.Name] is IEnumerable;
                });

                if (info == null)
                {
                    throw new Exception(string.Format(ProgramArgumentsStrings.NoMatchingPositionalArgument, arg));
                }

                boolOrStringValue = arg;
            }

            // Convert and set the value
            object value = GetConverter(info).Convert(
                boolOrStringValue, info.Type.HasElementType ? info.Type.GetElementType() : info.Type
            );
            if ((typeof(IEnumerable).IsAssignableFrom(info.Type)) && (info.Type != typeof(string)))
            {
                if (!values.ContainsKey(info.Name)) { values[info.Name] = new ArrayList(); }
                ((ArrayList)values[info.Name]).Add(value);
            }
            else
            {
                values[info.Name] = value;
            }
        }

        IEnumerable<ProgramArgumentInfo> GetInfos()
        {
            foreach (var info in infos) { yield return info; }
            foreach (var info in StandardInfos) { yield return info; }
        }

        static IEnumerable<string> SplitArguments(string line)
        {
            var currentArg = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char currentChar = line[i];
                switch (currentChar)
                {
                    // whitespace
                    case ' ':
                    case '\t':
                        if (currentArg.Length != 0)
                        {
                            // we're in an arg, terminate it and reset currentArg buffer
                            yield return currentArg.ToString();
                            currentArg = new StringBuilder();
                        }
                        else
                        {
                            // we're not in an arg, so just strip whitespace
                        }
                        break;

                    // quote
                    case '"':
                        int closeQuoteIndex = IndexOfCloseQuote(line, i);
                        if (closeQuoteIndex == -1)
                        {
                            throw new Exception(
                                string.Format(
                                    ProgramArgumentsStrings.ArgumentContainsUnmatchedQuote, currentArg.ToString()
                                )
                            );
                        }

                        string stringVal = line.Substring(i + 1, closeQuoteIndex - i - 1);
                        // quoted strings may have a prefix (e.g., /out:"Foo Bar.dll") so append to any existing prefix
                        currentArg.Append(stringVal);
                        // however, quoted strings may not appear themselves as a 
                        // prefix (e.g., /out:"Foo Bar".dll) so terminate - this is consistent with CSC.exe
                        yield return currentArg.ToString();
                        currentArg = new StringBuilder();
                        // eat the chars
                        i = closeQuoteIndex + 1;

                        break;

                    default:
                        // simply append the current char
                        currentArg.Append(currentChar);
                        break;
                }

            }

            // deal with the trailing argument
            if (currentArg.Length > 0) { yield return currentArg.ToString(); }
        }

        static int IndexOfCloseQuote(string s, int openQuoteIndex)
        {
            for (int i = openQuoteIndex + 1; i < s.Length; i++)
            {
                if (s[i] != '"') { continue; }
                if (i == s.Length - 1) { return i; }
                if (s[i + 1] != '"') { return i; }

                // we're on an escape, so advance the cursor past the second "
                i++;
            }

            // if we get here, then we didn't find a matching close quote
            return -1;
        }

        // Split things like "/foo=bar" and "-baz:quux" into "/foo" and "bar" and "-baz" and "quux" before
        // we process them, so all flags with values look at "next arg" uniformly
        static IEnumerable<string> SplitArgumentNames(IEnumerable<string> args)
        {
            foreach (string arg in args)
            {
                char[] seps = new char[] { '=', ':' };
                if (IsSwitch(arg))
                {
                    int sepIndex = arg.IndexOfAny(seps);
                    if (sepIndex == -1) { yield return arg; }
                    else
                    {
                        yield return arg.Substring(0, sepIndex).TrimEnd();
                        if (sepIndex + 1 != arg.Length) { yield return arg.Substring(sepIndex + 1).TrimStart(); }
                    }
                }
                else
                {
                    yield return arg;
                }

            }

        }

        static bool IsSwitch(string arg)
        {
            // Allow argument escaping, e.g. //foo or --bar is really "/foo" or "-bar" as positional arg
            if (arg.StartsWith("-") && !arg.StartsWith("--")) { return true; }
            if (arg.StartsWith("/") && !arg.StartsWith("//")) { return true; }
            return false;
        }

    }

}
