using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WPFViewer
{
    public class LaunchPreset
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Args { get; set; } = new();

        public override string ToString() => Name;
    }
}
