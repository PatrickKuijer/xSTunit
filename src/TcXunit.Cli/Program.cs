using System;

namespace TcXunit.Cli
{
    public static class Program
    {
        public static int Main(string[] args) => CliRunner.Run(args, Console.Out);
    }
}
