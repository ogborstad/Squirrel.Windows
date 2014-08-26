﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mono.Options;
using Squirrel;

namespace Squirrel.Update
{
    enum UpdateAction {
        Unset = 0, Install, Uninstall, Download, Update,
    }

    class Program
    {
        static OptionSet opts;

        static int Main(string[] args)
        {
            if (args.Any(x => x.StartsWith("/squirrel", StringComparison.OrdinalIgnoreCase))) {
                // NB: We're marked as Squirrel-aware, but we don't want to do
                // anything in response to these events
                return 0;
            }

            bool silentInstall = false;
            var updateAction = default(UpdateAction);
            string target = default(string);

            opts = new OptionSet() {
                "Usage: Update.exe command [OPTS]",
                "Manages Squirrel packages",
                "",
                "Commands",
                { "install=", "Install the app whose package is in the specified directory", v => { updateAction = UpdateAction.Install; target = v; } },
                { "uninstall", "Uninstall the app the same dir as Update.exe", v => updateAction = UpdateAction.Uninstall},
                { "download=", "Download the releases specified by the URL and write new results to stdout as JSON", v => { updateAction = UpdateAction.Download; target = v; } },
                { "update=", "Update the application to the latest remote version specified by URL", v => { updateAction = UpdateAction.Update; target = v; } },
                "",
                "Options:",
                { "h|?|help", "Display Help and exit", _ => ShowHelp() },
                { "s|silent", "Silent install", _ => silentInstall = true},
            };

            opts.Parse(args);

            if (updateAction == UpdateAction.Unset) {
                ShowHelp();
            }

            switch (updateAction) {
            case UpdateAction.Install:
                Install(silentInstall, target).Wait();
                break;
            case UpdateAction.Update:
                Update(target).Wait();
                break;
            }

            return 0;
        }

        public static async Task Install(bool silentInstall, string sourceDirectory = null)
        {
            sourceDirectory = sourceDirectory ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var releasesPath = Path.Combine(sourceDirectory, "RELEASES"); 

            if (!File.Exists(releasesPath)) {
                var nupkgs = (new DirectoryInfo(sourceDirectory)).GetFiles()
                    .Where(x => x.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Select(x => ReleaseEntry.GenerateFromFile(x.FullName));

                ReleaseEntry.WriteReleaseFile(nupkgs, releasesPath);
            }

            var ourAppName = ReleaseEntry.ParseReleaseFile(releasesPath).First().PackageName;
            using (var mgr = new UpdateManager(sourceDirectory, ourAppName, FrameworkVersion.Net45)) {
                await mgr.FullInstall(silentInstall);
            }
        }

        public static async Task Update(string updateUrl, string appName = null)
        {
            appName = appName ?? getAppNameFromDirectory();

            using (var mgr = new UpdateManager(updateUrl, appName, FrameworkVersion.Net45)) {
                var updateInfo = await mgr.CheckForUpdate(progress: x => Console.WriteLine(x / 3));
                await mgr.DownloadReleases(updateInfo.ReleasesToApply, x => Console.WriteLine(33 + x / 3));
                await mgr.ApplyReleases(updateInfo, x => Console.WriteLine(66 + x / 3));
            }
        }

        public static void ShowHelp()
        {
            opts.WriteOptionDescriptions(Console.Out);
            Environment.Exit(0);
        }

        static string getAppNameFromDirectory(string path = null)
        {
            path = path ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return (new DirectoryInfo(path)).Name;
        }
    }
}
