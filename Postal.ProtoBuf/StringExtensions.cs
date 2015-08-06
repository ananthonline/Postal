using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Postal.ProtoBuf
{
    public static class StringExtensions
    {
        public static string ReplaceAll(this string str, IDictionary<string, string> replacements)
        { 
            var final = str;
            foreach (var kvp in replacements)
                final = final.Replace(kvp.Key, kvp.Value);

            return final;
        }
    }
}