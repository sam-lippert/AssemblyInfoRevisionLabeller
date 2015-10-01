using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Exortech.NetReflector;
using ThoughtWorks.CruiseControl.Core;

namespace CCNet.AssemblyInfoRevisionLabeller.Plugin
{
    [ReflectorType("assemblyInfoRevisionLabeller")]
    public class AssemblyInfoRevisionLabeller : ILabeller
    {
        public string Generate(IIntegrationResult previousResult)
        {
            Regex regex = new Regex(Pattern);

            string content = string.Join(Environment.NewLine,
                File.ReadAllLines(Path).Where(l => !l.Trim().StartsWith("//")).ToArray());

            if (WorkingDirectory == null)
            {
                var assemblyInfoFolder = new FileInfo(Path).Directory;
                if (assemblyInfoFolder?.Name == "Properties")
                {
                    assemblyInfoFolder = assemblyInfoFolder.Parent;
                }
                WorkingDirectory = assemblyInfoFolder?.FullName;
            }

            string revision = Exec("git", "rev-list --count HEAD", WorkingDirectory);
            bool svn = string.IsNullOrEmpty(revision);
            if (svn)
            {
                if (AutoGetSource) Exec("svn", "update", WorkingDirectory);
                revision = Exec("svn", "info --show-item revision", WorkingDirectory);
            }
            else if (AutoGetSource)
            {
                Exec("git", "pull", WorkingDirectory);
                revision = Exec("git", "rev-list --count HEAD", WorkingDirectory);
            }

            var versionMatch = regex.Match(content).Groups["version"].Value.TrimEnd('.', '*');
            Version version;
            try
            {
                version = new Version(versionMatch);
            }
            catch (Exception)
            {
                throw new Exception($"Could not read version format for <{Path}>: {versionMatch}");
            }

            int revNum;
            if (!int.TryParse(revision, out revNum))
            {
                throw new Exception($"Could not read revision from {(svn ? "svn" : "git")} working copy <{WorkingDirectory}>: {revision}");
            }

            return new Version(Math.Max(0, version.Major),
                               Math.Max(0, version.Minor),
                               Math.Max(0, version.Build),
                               revNum)
                           .ToString();
        }

        public void Run(IIntegrationResult result)
        {
            result.Label = Generate(result);
        }

        [ReflectorProperty("path", Required = true)]
        public string Path { get; set; }

        [ReflectorProperty("workingDirectory", Required = false)]
        public string WorkingDirectory { get; set; }

        [ReflectorProperty("autoGetSource", Required = false)]
        public bool AutoGetSource { get; set; }

        [ReflectorProperty("pattern", Required = false)]
        public string Pattern { get; set; } = @"AssemblyVersion\(""(?<version>[^\""]*)""\)";

        private static string Exec(string command, string args, string dir)
        {
            var p = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    FileName = command,
                    Arguments = args,
                    CreateNoWindow = true,
                    WorkingDirectory = dir,
                }
            };
            p.Start();

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output.Trim();
        }
    }
}