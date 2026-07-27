using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TcXunitResultsSpike.TestRunner;

namespace TcXunitResultsSpike
{
    /// <summary>
    /// THROWAWAY SPIKE. First cut just proves the process-exec + JSON +
    /// tool-window-render loop works inside XAE Shell - no click-to-navigate
    /// into the .TcPOU editor yet (see README.md).
    /// </summary>
    public partial class ResultsToolWindowControl : UserControl
    {
        public ResultsToolWindowControl()
        {
            this.InitializeComponent();
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            this.ResultsTree.Items.Clear();
            this.StatusText.Text = "Running...";

            try
            {
                // TODO(spike): resolve this from the open .plcproj's directory via
                // the XAE DTE/solution service instead of the current directory,
                // once running inside an actual XAE Shell session.
                var directory = Environment.CurrentDirectory;
                var config = TcxunitConfig.Load(directory);
                var runner = new TcxunitProcessRunner();
                var result = runner.Run(config, directory);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    this.StatusText.Text = "Error: " + result.Error;
                    return;
                }

                this.StatusText.Text = $"Passed: {result.Passed}  Failed: {result.Failed}  (exit code {result.ExitCode})";

                foreach (var suite in result.Suites ?? new System.Collections.Generic.List<TcxunitSuiteResult>())
                {
                    var suiteItem = new TreeViewItem { Header = suite.Name, IsExpanded = true };

                    if (!string.IsNullOrEmpty(suite.Error))
                    {
                        suiteItem.Items.Add(new TreeViewItem
                        {
                            Header = "ERROR: " + suite.Error,
                            Foreground = Brushes.Red,
                        });
                    }

                    foreach (var test in suite.Tests ?? new System.Collections.Generic.List<TcxunitTestResult>())
                    {
                        var testItem = new TreeViewItem
                        {
                            Header = (test.Passed ? "[PASS] " : "[FAIL] ") + test.Name,
                            Foreground = test.Passed ? Brushes.Green : Brushes.Red,
                        };

                        foreach (var failure in test.Failures ?? new System.Collections.Generic.List<string>())
                        {
                            testItem.Items.Add(new TreeViewItem { Header = failure });
                        }

                        suiteItem.Items.Add(testItem);
                    }

                    this.ResultsTree.Items.Add(suiteItem);
                }
            }
            catch (Exception ex)
            {
                this.StatusText.Text = "Error: " + ex.Message;
            }
        }
    }
}
