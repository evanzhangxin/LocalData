using System;
using System.Collections.Generic;

namespace SlnGen
{
    public class Util
    {
        static private char[] _quoteChars = new char[] { '"' };

        static public List<string> ExtractQouted(string toExtract)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(toExtract)) return result;

            string[] quotedSplit = toExtract.Split(_quoteChars);

            for (int i = 0; i < quotedSplit.Length; i++)
            {
                // if we've seen an even number of quotes, we are outside of a quoted region
                // otherwise, we are inside. If we are outside, split on spaces.
                if ((i % 2) == 0)
                    result.AddRange(quotedSplit[i].Split((char[]) null, StringSplitOptions.RemoveEmptyEntries));
                else
                    result.Add(quotedSplit[i]);
            }

            return result;
        }
    }
}
