using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace TcXunitResultsSpike
{
    [Guid("c6896902-8d96-4182-b319-7242b5730161")]
    public sealed class ResultsToolWindow : ToolWindowPane
    {
        public ResultsToolWindow() : base(null)
        {
            this.Caption = "TcXunit Results";
            this.Content = new ResultsToolWindowControl();
        }
    }
}
