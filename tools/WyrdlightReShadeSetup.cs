using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[assembly: AssemblyTitle("Wyrdlight ReShade Setup")]
[assembly: AssemblyDescription("Configures Wyrdlight ReShade.")]
[assembly: AssemblyCompany("Wyrdlight")]
[assembly: AssemblyProduct("Wyrdlight ReShade Setup")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace WyrdlightReShadeSetup
{
    internal static class Program
    {
        private const string ReShadeHomeUrl = "https://reshade.me/";
        private const string ReShadeDownloadBaseUrl = "https://reshade.me";
        private const string KnownLatestVersion = "6.7.3";
        private const string FallbackVersion = "6.6.2";
        private const string PersonalScreenshotPath = @"C:\Google Drive\Pictures\Afterburner";

        private static readonly string[] PayloadFiles = { "reshade.ini", "Wyrdlight.ini" };
        private static readonly string PayloadShaderDirectory = "reshade-shaders";

        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            bool personalPreset = ShouldUsePersonalPreset(args);
            bool skipDownload = ShouldSkipDownload(args);

            if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                              a.Equals("/?", StringComparison.OrdinalIgnoreCase)))
            {
                ShowHelp();
                return 0;
            }

            try
            {
                PrintHeader(personalPreset);

                string toolDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                SetupPaths setupPaths = personalPreset
                    ? ChoosePersonalSetupPaths(toolDirectory)
                    : ChooseSetupPaths(toolDirectory);

                if (setupPaths.CopyPayloadToOutput)
                {
                    CopyPayloadIfNeeded(toolDirectory, setupPaths.OutputDirectory);
                }

                DownloadChoice downloadChoice = skipDownload
                    ? new DownloadChoice(false, false, null, true)
                    : personalPreset
                        ? new DownloadChoice(true, true, null, false)
                        : ChooseReShadeBuild();
                if (!downloadChoice.SkipDownload)
                {
                    DownloadAndInstallReShade(downloadChoice, setupPaths.OutputDirectory);
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("Skipping ReShade DLL download.");
                }

                KeySet keys = personalPreset ? CreateWyrdlightKeySet() : ChooseKeySet();
                string screenshotPath = personalPreset
                    ? PersonalScreenshotPath
                    : ChooseScreenshotFolder(setupPaths.SkyrimDirectory);

                if (personalPreset)
                {
                    Console.WriteLine();
                    Console.WriteLine("Personal preset selected:");
                    Console.WriteLine("  ReShade:    " + (skipDownload ? "No download (--no-download)" : "Latest official full add-on build"));
                    Console.WriteLine("  Keys:       Scroll Lock, Ctrl+F12, F11");
                    Console.WriteLine("  Screenshots: " + screenshotPath);
                }

                ConfigureReShadeIni(setupPaths.OutputDirectory, setupPaths.SkyrimDirectory, keys, screenshotPath);

                Console.WriteLine();
                Console.WriteLine("Done. Wyrdlight ReShade files are configured in:");
                Console.WriteLine(setupPaths.OutputDirectory);
                if (!personalPreset && !PathsEqual(setupPaths.OutputDirectory, setupPaths.SkyrimDirectory))
                {
                    Console.WriteLine();
                    Console.WriteLine("ReShade paths point at this Skyrim folder:");
                    Console.WriteLine(setupPaths.SkyrimDirectory);
                    Console.WriteLine("Launch through MO2 as usual so Root Builder can deploy this mod's root files.");
                }
                Console.WriteLine();
                Console.WriteLine("Files set up:");
                Console.WriteLine("  dxgi.dll        ReShade, unless you chose to skip the download");
                Console.WriteLine("  reshade.ini     Paths, screenshots, proxy setting, and keys");
                Console.WriteLine("  Wyrdlight.ini   Preset");
                Console.WriteLine();
                if (personalPreset)
                {
                    Console.WriteLine("Launch the game from this folder.");
                }
                else if (PathsEqual(setupPaths.OutputDirectory, setupPaths.SkyrimDirectory))
                {
                    Console.WriteLine("Launch Skyrim from this same folder, or through your modlist launcher.");
                }
                else
                {
                    Console.WriteLine("Launch Skyrim through MO2 as usual; Root Builder should deploy this mod's root files.");
                }
                PauseBeforeExit();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Setup stopped:");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine(personalPreset
                    ? "If the game or another tool is open, close it and try again. If the folder is protected, run this tool as administrator."
                    : "If Skyrim or a mod manager is open, close it and try again. If the folder is protected, run this tool as administrator.");
                PauseBeforeExit();
                return 1;
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Wyrdlight ReShade Setup");
            Console.WriteLine();
            Console.WriteLine("Run this EXE from the Wyrdlight ReShade mod/root folder or from the folder");
            Console.WriteLine("that contains SkyrimSE.exe. The wizard can detect your Skyrim or Stock Game");
            Console.WriteLine("folder, download an official ReShade build, install it as dxgi.dll, and");
            Console.WriteLine("rewrite paths inside reshade.ini.");
            Console.WriteLine();
            Console.WriteLine("Use --no-download to configure reshade.ini without downloading dxgi.dll.");
        }

        private static void PrintHeader(bool personalPreset)
        {
            Console.WriteLine("Wyrdlight ReShade Setup");
            Console.WriteLine("========================");
            Console.WriteLine();
            if (personalPreset)
            {
                Console.WriteLine("Personal fast path is active.");
                Console.WriteLine("This applies your personal Wyrdlight ReShade defaults to the folder this EXE is in.");
                Console.WriteLine("No Skyrim or Stock Game detection will run.");
            }
            else
            {
                Console.WriteLine("This sets up Wyrdlight's ReShade files for Skyrim.");
                Console.WriteLine("Run it from the Wyrdlight mod/root folder for MO2 Root Builder, or from the");
                Console.WriteLine("folder that contains SkyrimSE.exe for a direct install.");
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        private static bool ShouldUsePersonalPreset(string[] args)
        {
            if (args.Any(a => a.Equals("--no-personal", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (args.Any(a => a.Equals("--personal", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return (Control.ModifierKeys & Keys.Control) == Keys.Control;
        }

        private static bool ShouldSkipDownload(string[] args)
        {
            return args.Any(a => a.Equals("--no-download", StringComparison.OrdinalIgnoreCase) ||
                                 a.Equals("--skip-download", StringComparison.OrdinalIgnoreCase));
        }

        private static SetupPaths ChoosePersonalSetupPaths(string toolDirectory)
        {
            Console.WriteLine("Personal preset will configure the current folder:");
            Console.WriteLine(toolDirectory);
            Console.WriteLine("No Skyrim or Stock Game detection will be used.");
            Console.WriteLine();
            return new SetupPaths(toolDirectory, toolDirectory, false);
        }

        private static SetupPaths ChooseSetupPaths(string toolDirectory)
        {
            if (LooksLikeSkyrimRoot(toolDirectory))
            {
                Console.WriteLine("This folder looks like a Skyrim game root:");
                Console.WriteLine(toolDirectory);
                return new SetupPaths(toolDirectory, toolDirectory, false);
            }

            bool toolFolderHasPayload = HasPayload(toolDirectory);
            DetectionResult detected = DetectSkyrimDirectory(toolDirectory);

            if (toolFolderHasPayload)
            {
                Console.WriteLine("This folder looks like the Wyrdlight ReShade mod/root folder:");
                Console.WriteLine(toolDirectory);
                Console.WriteLine();

                string skyrimDirectory = null;
                if (detected != null)
                {
                    Console.WriteLine("Detected Skyrim folder:");
                    Console.WriteLine(detected.Path);
                    Console.WriteLine("Reason: " + detected.Reason);
                    if (Confirm("Use this Skyrim folder for ReShade paths?", true))
                    {
                        skyrimDirectory = detected.Path;
                    }
                    Console.WriteLine();
                }

                if (string.IsNullOrEmpty(skyrimDirectory))
                {
                    Console.WriteLine("Choose the Skyrim or Stock Game folder that contains SkyrimSE.exe.");
                    skyrimDirectory = AskForSkyrimDirectory(toolDirectory);
                    Console.WriteLine();
                }

                Console.WriteLine("Recommended for MO2 Root Builder: configure this mod folder, then let Root Builder deploy it.");
                Console.WriteLine("Answer No only if you want to copy/install the files directly into the Skyrim folder now.");
                if (Confirm("Configure this mod folder for Root Builder?", true))
                {
                    return new SetupPaths(toolDirectory, skyrimDirectory, false);
                }

                return new SetupPaths(skyrimDirectory, skyrimDirectory, true);
            }

            if (detected != null)
            {
                Console.WriteLine("Detected Skyrim folder:");
                Console.WriteLine(detected.Path);
                Console.WriteLine("Reason: " + detected.Reason);
                if (Confirm("Install directly into this Skyrim folder?", true))
                {
                    return new SetupPaths(detected.Path, detected.Path, false);
                }
            }

            Console.WriteLine("I could not safely identify the target folder from here.");
            string targetDirectory = AskForSkyrimDirectory(toolDirectory);
            return new SetupPaths(targetDirectory, targetDirectory, false);
        }

        private static string AskForSkyrimDirectory(string initialDirectory)
        {
            while (true)
            {
                Console.WriteLine("Where is your Skyrim or Stock Game folder?");
                Console.WriteLine("  1. Browse for the folder");
                Console.WriteLine("  2. Type or paste the folder path");
                Console.WriteLine();

                string choice = ReadText("Choice", "1");
                string folder = null;

                if (choice == "1")
                {
                    folder = BrowseForFolder(initialDirectory);
                    if (string.IsNullOrEmpty(folder))
                    {
                        Console.WriteLine("No folder selected.");
                        Console.WriteLine();
                        continue;
                    }
                }
                else if (choice == "2")
                {
                    folder = ReadText("Paste the Skyrim or Stock Game folder path", "");
                }
                else
                {
                    Console.WriteLine("Please choose 1 or 2.");
                    Console.WriteLine();
                    continue;
                }

                folder = ResolveFolderInput(folder);
                if (!Directory.Exists(folder))
                {
                    Console.WriteLine("That folder does not exist:");
                    Console.WriteLine(folder);
                    Console.WriteLine();
                    continue;
                }

                if (!LooksLikeSkyrimRoot(folder))
                {
                    Console.WriteLine();
                    Console.WriteLine("I do not see SkyrimSE.exe, Skyrim.exe, or skse64_loader.exe in:");
                    Console.WriteLine(folder);
                    Console.WriteLine("Regular users should choose the Skyrim or Stock Game folder.");
                    if (!Confirm("Use this folder anyway?", false))
                    {
                        Console.WriteLine();
                        continue;
                    }
                }

                return Path.GetFullPath(folder);
            }
        }

        private static bool HasPayload(string folder)
        {
            return PayloadFiles.All(f => File.Exists(Path.Combine(folder, f))) &&
                   Directory.Exists(Path.Combine(folder, PayloadShaderDirectory));
        }

        private static string BrowseForFolder(string initialDirectory)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the Skyrim Stock Game folder or the folder containing SkyrimSE.exe.";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(initialDirectory))
                {
                    dialog.SelectedPath = initialDirectory;
                }

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    return dialog.SelectedPath;
                }
            }

            return null;
        }

        private static string ResolveFolderInput(string input)
        {
            string path = ResolveFolderPath(input);

            if (File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(Path.GetFullPath(path));
            }

            return path;
        }

        private static string ResolveFolderPath(string input)
        {
            string path = (input ?? string.Empty).Trim().Trim('"');
            if (path.Length == 0)
            {
                return path;
            }

            path = Environment.ExpandEnvironmentVariables(path);
            return Path.GetFullPath(path);
        }

        private static bool LooksLikeSkyrimRoot(string folder)
        {
            return File.Exists(Path.Combine(folder, "SkyrimSE.exe")) ||
                   File.Exists(Path.Combine(folder, "Skyrim.exe")) ||
                   File.Exists(Path.Combine(folder, "SkyrimSELauncher.exe")) ||
                   File.Exists(Path.Combine(folder, "skse64_loader.exe"));
        }

        private static DetectionResult DetectSkyrimDirectory(string startDirectory)
        {
            var candidates = new List<DetectionResult>();

            AddCandidate(candidates, startDirectory, "current folder");

            foreach (string ancestor in EnumerateAncestors(startDirectory))
            {
                AddCandidate(candidates, Path.Combine(ancestor, "Stock Game"), "nearby Stock Game folder");
                AddCandidate(candidates, Path.Combine(ancestor, "Skyrim Special Edition"), "nearby Skyrim Special Edition folder");
                AddCandidate(candidates, Path.Combine(ancestor, "Skyrim Anniversary Edition"), "nearby Skyrim Anniversary Edition folder");
                AddCandidate(candidates, Path.Combine(ancestor, "Game Root"), "nearby Game Root folder");
                AddCandidate(candidates, Path.Combine(ancestor, "Game"), "nearby Game folder");

                string mo2IniPath = Path.Combine(ancestor, "ModOrganizer.ini");
                foreach (string gamePath in ReadMo2GamePaths(mo2IniPath))
                {
                    AddCandidate(candidates, gamePath, "ModOrganizer.ini gamePath");
                }
            }

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            AddCandidate(candidates, Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Skyrim Special Edition"), "default Steam path");
            AddCandidate(candidates, Path.Combine(programFiles, "Steam", "steamapps", "common", "Skyrim Special Edition"), "default Steam path");
            AddCandidate(candidates, Path.Combine(Path.GetPathRoot(Path.GetFullPath(startDirectory)) ?? "C:\\", "Steam", "steamapps", "common", "Skyrim Special Edition"), "drive Steam path");

            if (candidates.Count == 0)
            {
                return null;
            }

            DetectionResult stockGame = candidates.FirstOrDefault(c => Path.GetFileName(c.Path).Equals("Stock Game", StringComparison.OrdinalIgnoreCase));
            if (stockGame != null)
            {
                return stockGame;
            }

            return candidates[0];
        }

        private static IEnumerable<string> EnumerateAncestors(string startDirectory)
        {
            DirectoryInfo current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }

        private static IEnumerable<string> ReadMo2GamePaths(string mo2IniPath)
        {
            if (!File.Exists(mo2IniPath))
            {
                yield break;
            }

            foreach (string rawLine in File.ReadLines(mo2IniPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex < 0)
                {
                    continue;
                }

                string key = line.Substring(0, equalsIndex).Trim();
                if (!key.Equals("gamePath", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("game_path", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("managedGamePath", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = DecodeIniPathValue(line.Substring(equalsIndex + 1), Path.GetDirectoryName(mo2IniPath));
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }
            }
        }

        private static string DecodeIniPathValue(string rawValue, string baseDirectory)
        {
            string value = (rawValue ?? string.Empty).Trim().Trim('"');
            if (value.Length == 0 || value.StartsWith("@", StringComparison.Ordinal))
            {
                return null;
            }

            value = Uri.UnescapeDataString(value);
            value = Environment.ExpandEnvironmentVariables(value);
            value = value.Replace('/', Path.DirectorySeparatorChar);

            if (!Path.IsPathRooted(value) && !string.IsNullOrEmpty(baseDirectory))
            {
                value = Path.Combine(baseDirectory, value);
            }

            return value;
        }

        private static void AddCandidate(List<DetectionResult> candidates, string path, string reason)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                return;
            }

            if (!Directory.Exists(fullPath) || !LooksLikeSkyrimRoot(fullPath))
            {
                return;
            }

            string normalized = NormalizeDirectory(fullPath);
            if (candidates.Any(c => NormalizeDirectory(c.Path).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(new DetectionResult(fullPath, reason));
        }

        private static string NormalizeDirectory(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void CopyPayloadIfNeeded(string sourceDirectory, string targetDirectory)
        {
            if (PathsEqual(sourceDirectory, targetDirectory))
            {
                return;
            }

            bool sourceHasPayload = HasPayload(sourceDirectory);
            if (!sourceHasPayload)
            {
                Console.WriteLine();
                Console.WriteLine("The tool folder does not contain the Wyrdlight payload files, so nothing will be copied.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("The setup tool is not in the target folder.");
            Console.WriteLine("I can copy Wyrdlight.ini, reshade.ini, and reshade-shaders into:");
            Console.WriteLine(targetDirectory);
            if (!Confirm("Copy Wyrdlight files there?", true))
            {
                return;
            }

            foreach (string file in PayloadFiles)
            {
                CopyFileWithBackup(Path.Combine(sourceDirectory, file), Path.Combine(targetDirectory, file));
            }

            CopyDirectory(Path.Combine(sourceDirectory, PayloadShaderDirectory),
                          Path.Combine(targetDirectory, PayloadShaderDirectory));
        }

        private static DownloadChoice ChooseReShadeBuild()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Which ReShade build should be installed as dxgi.dll?");
                Console.WriteLine("  1. Latest official full add-on build (recommended for Wyrdlight and Skyrim)");
                Console.WriteLine("  2. ReShade 6.6.2 full add-on build (proven fallback)");
                Console.WriteLine("  3. Latest official standard build");
                Console.WriteLine("  4. ReShade 6.6.2 standard build");
                Console.WriteLine("  5. Do not download a DLL; configure reshade.ini only");
                Console.WriteLine();
                Console.WriteLine("Use full add-on support for single-player Skyrim modlists. Use standard only if you know you do not need add-ons.");

                string choice = ReadText("Choice", "1");
                if (choice == "1")
                {
                    return new DownloadChoice(true, true, null, false);
                }
                if (choice == "2")
                {
                    return new DownloadChoice(false, true, FallbackVersion, false);
                }
                if (choice == "3")
                {
                    return new DownloadChoice(true, false, null, false);
                }
                if (choice == "4")
                {
                    return new DownloadChoice(false, false, FallbackVersion, false);
                }
                if (choice == "5")
                {
                    return new DownloadChoice(false, false, null, true);
                }

                Console.WriteLine("Please choose 1, 2, 3, 4, or 5.");
            }
        }

        private static KeySet ChooseKeySet()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Choose shortcut keys:");
                Console.WriteLine("  1. Wyrdlight defaults: effects Scroll Lock, menu Ctrl+F12, screenshot F11");
                Console.WriteLine("  2. ReShade defaults: effects Scroll Lock, menu Home, screenshot Print Screen");
                Console.WriteLine("  3. Pick common keys for each action");

                string choice = ReadText("Choice", "1");
                if (choice == "1")
                {
                    return CreateWyrdlightKeySet();
                }
                if (choice == "2")
                {
                    return new KeySet(
                        new KeyBinding("Scroll Lock", "145,0,0,0"),
                        new KeyBinding("Home", "36,0,0,0"),
                        new KeyBinding("Print Screen", "44,0,0,0"));
                }
                if (choice == "3")
                {
                    KeyBinding effects = ChooseBinding("effect toggle", new[]
                    {
                        new KeyBinding("Scroll Lock", "145,0,0,0"),
                        new KeyBinding("End", "35,0,0,0"),
                        new KeyBinding("F10", "121,0,0,0"),
                        new KeyBinding("Disabled", "0,0,0,0")
                    });

                    KeyBinding menu = ChooseBinding("ReShade menu", new[]
                    {
                        new KeyBinding("Ctrl+F12", "123,1,0,0"),
                        new KeyBinding("Home", "36,0,0,0"),
                        new KeyBinding("Shift+F2", "113,0,1,0"),
                        new KeyBinding("F12", "123,0,0,0")
                    });

                    KeyBinding screenshot = ChooseBinding("screenshot", new[]
                    {
                        new KeyBinding("F11", "122,0,0,0"),
                        new KeyBinding("Print Screen", "44,0,0,0"),
                        new KeyBinding("F12", "123,0,0,0"),
                        new KeyBinding("Disabled", "0,0,0,0")
                    });

                    return new KeySet(effects, menu, screenshot);
                }

                Console.WriteLine("Please choose 1, 2, or 3.");
            }
        }

        private static KeySet CreateWyrdlightKeySet()
        {
            return new KeySet(
                new KeyBinding("Scroll Lock", "145,0,0,0"),
                new KeyBinding("Ctrl+F12", "123,1,0,0"),
                new KeyBinding("F11", "122,0,0,0"));
        }

        private static string ChooseScreenshotFolder(string skyrimDirectory)
        {
            string gameScreenshots = Path.Combine(skyrimDirectory, "Screenshots");
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string picturesScreenshots = string.IsNullOrEmpty(pictures)
                ? gameScreenshots
                : Path.Combine(pictures, "Wyrdlight ReShade");
            string desktopScreenshots = string.IsNullOrEmpty(desktop)
                ? gameScreenshots
                : Path.Combine(desktop, "Wyrdlight ReShade Screenshots");

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Choose screenshot folder:");
                Console.WriteLine("  1. Skyrim or Stock Game Screenshots folder (recommended)");
                Console.WriteLine("     " + gameScreenshots);
                Console.WriteLine("  2. Pictures\\Wyrdlight ReShade");
                Console.WriteLine("     " + picturesScreenshots);
                Console.WriteLine("  3. Desktop\\Wyrdlight ReShade Screenshots");
                Console.WriteLine("     " + desktopScreenshots);
                Console.WriteLine("  4. Browse for a folder");
                Console.WriteLine("  5. Type or paste a folder path");

                string choice = ReadText("Choice", "1");
                string folder = null;

                if (choice == "1")
                {
                    folder = gameScreenshots;
                }
                else if (choice == "2")
                {
                    folder = picturesScreenshots;
                }
                else if (choice == "3")
                {
                    folder = desktopScreenshots;
                }
                else if (choice == "4")
                {
                    folder = BrowseForScreenshotFolder(gameScreenshots);
                    if (string.IsNullOrEmpty(folder))
                    {
                        Console.WriteLine("No folder selected.");
                        continue;
                    }
                }
                else if (choice == "5")
                {
                    folder = ReadText("Paste the screenshot folder path", "");
                }
                else
                {
                    Console.WriteLine("Please choose 1, 2, 3, 4, or 5.");
                    continue;
                }

                folder = ResolveFolderPath(folder);
                if (string.IsNullOrEmpty(folder))
                {
                    Console.WriteLine("Please choose a folder.");
                    continue;
                }

                if (!Directory.Exists(folder))
                {
                    Console.WriteLine("This folder does not exist:");
                    Console.WriteLine(folder);
                    if (!Confirm("Create it?", true))
                    {
                        continue;
                    }
                }

                return folder;
            }
        }

        private static string BrowseForScreenshotFolder(string initialDirectory)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where ReShade screenshots should be saved.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(initialDirectory))
                {
                    dialog.SelectedPath = initialDirectory;
                }

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    return dialog.SelectedPath;
                }
            }

            return null;
        }

        private static KeyBinding ChooseBinding(string label, KeyBinding[] options)
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("Choose " + label + " key:");
                for (int i = 0; i < options.Length; i++)
                {
                    Console.WriteLine("  " + (i + 1) + ". " + options[i].Name);
                }

                string choice = ReadText("Choice", "1");
                int index;
                if (int.TryParse(choice, out index) && index >= 1 && index <= options.Length)
                {
                    return options[index - 1];
                }

                Console.WriteLine("Please choose a number from the list.");
            }
        }

        private static void DownloadAndInstallReShade(DownloadChoice choice, string targetDirectory)
        {
            ReshadeDownload download = ResolveDownload(choice);

            if (download.Version == "6.7.0" || download.Version == "6.7.1")
            {
                Console.WriteLine();
                Console.WriteLine("Warning: ReShade " + download.Version + " had reports of ENB wrapper problems.");
                Console.WriteLine("ReShade 6.7.2 and newer fixed D3D hooking when other wrappers are present.");
                if (Confirm("Use ReShade 6.6.2 full add-on fallback instead?", true))
                {
                    download = new ReshadeDownload(BuildVersionedSetupUrl(FallbackVersion, true), FallbackVersion, true);
                }
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "WyrdlightReShadeSetup");
            Directory.CreateDirectory(tempDirectory);

            string setupPath = Path.Combine(tempDirectory, Path.GetFileName(new Uri(download.Url).LocalPath));
            string dllPath = Path.Combine(tempDirectory, "ReShade64_" + download.Version + (download.Addon ? "_Addon" : "") + ".dll");

            Console.WriteLine();
            Console.WriteLine("Downloading official ReShade setup:");
            Console.WriteLine(download.Url);

            using (var client = new WebClient())
            {
                client.Headers["User-Agent"] = "Wyrdlight ReShade Setup";
                client.DownloadFile(download.Url, setupPath);
            }

            Console.WriteLine("Extracting ReShade64.dll from the setup package...");
            ExtractEmbeddedZipEntry(setupPath, "ReShade64.dll", dllPath);

            string targetDll = Path.Combine(targetDirectory, "dxgi.dll");
            InstallDxgiDll(dllPath, targetDll);

            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(targetDll);
            Console.WriteLine("Installed dxgi.dll: ReShade " + (versionInfo.ProductVersion ?? download.Version));
        }

        private static ReshadeDownload ResolveDownload(DownloadChoice choice)
        {
            if (!choice.Latest)
            {
                return new ReshadeDownload(BuildVersionedSetupUrl(choice.Version, choice.Addon), choice.Version, choice.Addon);
            }

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers["User-Agent"] = "Wyrdlight ReShade Setup";
                    string html = client.DownloadString(ReShadeHomeUrl);
                    MatchCollection matches = Regex.Matches(
                        html,
                        "href=\"(?<href>/downloads/ReShade_Setup_(?<version>[0-9]+(?:\\.[0-9]+)*)(?<addon>_Addon)?\\.exe)\"",
                        RegexOptions.IgnoreCase);

                    ReshadeDownload best = null;
                    foreach (Match match in matches)
                    {
                        bool isAddon = match.Groups["addon"].Success;
                        if (isAddon != choice.Addon)
                        {
                            continue;
                        }

                        string version = match.Groups["version"].Value;
                        string url = ReShadeDownloadBaseUrl + match.Groups["href"].Value;
                        var candidate = new ReshadeDownload(url, version, isAddon);
                        if (best == null || CompareVersions(candidate.Version, best.Version) > 0)
                        {
                            best = candidate;
                        }
                    }

                    if (best != null)
                    {
                        return best;
                    }
                }
            }
            catch
            {
                Console.WriteLine("Could not read the latest version from reshade.me. Trying known latest " + KnownLatestVersion + ".");
            }

            return new ReshadeDownload(BuildVersionedSetupUrl(KnownLatestVersion, choice.Addon), KnownLatestVersion, choice.Addon);
        }

        private static string BuildVersionedSetupUrl(string version, bool addon)
        {
            return ReShadeDownloadBaseUrl + "/downloads/ReShade_Setup_" + version + (addon ? "_Addon" : "") + ".exe";
        }

        private static int CompareVersions(string left, string right)
        {
            Version leftVersion;
            Version rightVersion;
            if (Version.TryParse(left, out leftVersion) && Version.TryParse(right, out rightVersion))
            {
                return leftVersion.CompareTo(rightVersion);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static void ExtractEmbeddedZipEntry(string setupPath, string entryName, string outputPath)
        {
            byte[] setupBytes = File.ReadAllBytes(setupPath);
            int zipOffset = FindEmbeddedZipOffset(setupBytes);
            if (zipOffset < 0)
            {
                throw new InvalidDataException("Could not find the embedded ReShade archive in the downloaded setup EXE.");
            }

            using (var archiveStream = new MemoryStream(setupBytes, zipOffset, setupBytes.Length - zipOffset, false))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = archive.GetEntry(entryName);
                if (entry == null)
                {
                    throw new InvalidDataException("The downloaded setup EXE does not contain " + entryName + ".");
                }

                using (Stream input = entry.Open())
                using (Stream output = File.Create(outputPath))
                {
                    input.CopyTo(output);
                }
            }
        }

        private static int FindEmbeddedZipOffset(byte[] bytes)
        {
            for (int i = 0; i <= bytes.Length - 30; i++)
            {
                if (bytes[i] != 0x50 || bytes[i + 1] != 0x4B || bytes[i + 2] != 0x03 || bytes[i + 3] != 0x04)
                {
                    continue;
                }

                bool allZero = true;
                for (int j = 4; j < 30; j++)
                {
                    if (bytes[i + j] != 0)
                    {
                        allZero = false;
                        break;
                    }
                }

                if (!allZero)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void InstallDxgiDll(string sourceDll, string targetDll)
        {
            if (File.Exists(targetDll))
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(targetDll);
                bool looksLikeReShade = string.Equals(info.ProductName, "ReShade", StringComparison.OrdinalIgnoreCase);
                bool sameFile = FilesEqual(sourceDll, targetDll);

                if (sameFile)
                {
                    Console.WriteLine("dxgi.dll is already the selected ReShade build.");
                    return;
                }

                if (!looksLikeReShade)
                {
                    Console.WriteLine();
                    Console.WriteLine("A dxgi.dll already exists and does not identify itself as ReShade:");
                    Console.WriteLine(targetDll);
                    Console.WriteLine("Product name: " + (info.ProductName ?? "(unknown)"));
                    if (!Confirm("Back it up and replace it with ReShade?", false))
                    {
                        Console.WriteLine("Leaving existing dxgi.dll in place.");
                        return;
                    }
                }

                BackupFile(targetDll);
            }

            File.Copy(sourceDll, targetDll, true);
        }

        private static void ConfigureReShadeIni(string outputDirectory, string skyrimDirectory, KeySet keys, string screenshotPath)
        {
            string iniPath = Path.Combine(outputDirectory, "reshade.ini");
            if (!File.Exists(iniPath))
            {
                throw new FileNotFoundException("reshade.ini was not found in the folder being configured.", iniPath);
            }

            string presetPath = Path.Combine(skyrimDirectory, "Wyrdlight.ini");
            string shaderPath = Path.Combine(skyrimDirectory, "reshade-shaders", "Shaders");
            string texturePath = Path.Combine(skyrimDirectory, "reshade-shaders", "Textures");
            string cachePath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            Directory.CreateDirectory(cachePath);
            Directory.CreateDirectory(screenshotPath);

            var ini = IniDocument.Load(iniPath);
            ini.SetValue("GENERAL", "CurrentPresetPath", presetPath);
            ini.SetValue("GENERAL", "EffectSearchPaths", shaderPath);
            ini.SetValue("GENERAL", "IntermediateCachePath", cachePath);
            ini.SetValue("GENERAL", "PresetFiles", presetPath);
            ini.SetValue("GENERAL", "PresetPath", ".\\Wyrdlight.ini");
            ini.SetValue("GENERAL", "ScreenshotPath", screenshotPath);
            ini.SetValue("GENERAL", "TextureSearchPaths", texturePath);

            ini.SetValue("INPUT", "ForceShortcutModifiers", "1");
            ini.SetValue("INPUT", "InputProcessing", "1");
            ini.SetValue("INPUT", "KeyEffects", keys.Effects.Value);
            ini.SetValue("INPUT", "KeyMenu", keys.Menu.Value);
            ini.SetValue("INPUT", "KeyOverlay", keys.Menu.Value);
            ini.SetValue("INPUT", "KeyScreenshot", keys.Screenshot.Value);

            ini.SetValue("PROXY", "EnableProxyLibrary", "0");
            ini.SetValue("PROXY", "ProxyLibrary", "");

            ini.SetValue("SCREENSHOT", "PostSaveCommandWorkingDirectory", skyrimDirectory);
            ini.SetValue("SCREENSHOT", "SavePath", EnsureTrailingSlash(screenshotPath));

            ini.Save(iniPath);

            Console.WriteLine();
            Console.WriteLine("Configured reshade.ini in:");
            Console.WriteLine(outputDirectory);
            Console.WriteLine("ReShade paths point to:");
            Console.WriteLine(skyrimDirectory);
            Console.WriteLine("Keys:");
            Console.WriteLine("  Effects:    " + keys.Effects.Name);
            Console.WriteLine("  Menu:       " + keys.Menu.Name);
            Console.WriteLine("  Screenshot: " + keys.Screenshot.Name);
            Console.WriteLine("Screenshot folder:");
            Console.WriteLine(screenshotPath);
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (path.EndsWith("\\") || path.EndsWith("/"))
            {
                return path;
            }

            return path + "\\";
        }

        private static void CopyFileWithBackup(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination) && !FilesEqual(source, destination))
            {
                BackupFile(destination);
            }

            File.Copy(source, destination, true);
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }

            foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destination = Path.Combine(destinationDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
        }

        private static void BackupFile(string path)
        {
            string backupPath = path + ".wyrdlight-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int suffix = 1;
            while (File.Exists(backupPath))
            {
                backupPath = path + ".wyrdlight-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix;
                suffix++;
            }

            File.Copy(path, backupPath, false);
            Console.WriteLine("Backed up " + Path.GetFileName(path) + " to " + Path.GetFileName(backupPath));
        }

        private static bool FilesEqual(string left, string right)
        {
            FileInfo leftInfo = new FileInfo(left);
            FileInfo rightInfo = new FileInfo(right);
            if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            const int BufferSize = 8192;
            byte[] leftBuffer = new byte[BufferSize];
            byte[] rightBuffer = new byte[BufferSize];

            using (FileStream leftStream = File.OpenRead(left))
            using (FileStream rightStream = File.OpenRead(right))
            {
                while (true)
                {
                    int leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
                    int rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
                    if (leftRead != rightRead)
                    {
                        return false;
                    }
                    if (leftRead == 0)
                    {
                        return true;
                    }
                    for (int i = 0; i < leftRead; i++)
                    {
                        if (leftBuffer[i] != rightBuffer[i])
                        {
                            return false;
                        }
                    }
                }
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            string normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadText(string prompt, string defaultValue)
        {
            if (string.IsNullOrEmpty(defaultValue))
            {
                Console.Write(prompt + ": ");
            }
            else
            {
                Console.Write(prompt + " [" + defaultValue + "]: ");
            }

            string value = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return value.Trim();
        }

        private static bool Confirm(string prompt, bool defaultYes)
        {
            string suffix = defaultYes ? " [Y/n]: " : " [y/N]: ";
            Console.Write(prompt + suffix);
            string value = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultYes;
            }

            value = value.Trim();
            return value.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void PauseBeforeExit()
        {
            try
            {
                if (Console.IsInputRedirected)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            Console.WriteLine();
            Console.Write("Press Enter to close.");
            Console.ReadLine();
        }
    }

    internal sealed class SetupPaths
    {
        public SetupPaths(string outputDirectory, string skyrimDirectory, bool copyPayloadToOutput)
        {
            OutputDirectory = outputDirectory;
            SkyrimDirectory = skyrimDirectory;
            CopyPayloadToOutput = copyPayloadToOutput;
        }

        public string OutputDirectory { get; private set; }
        public string SkyrimDirectory { get; private set; }
        public bool CopyPayloadToOutput { get; private set; }
    }

    internal sealed class DetectionResult
    {
        public DetectionResult(string path, string reason)
        {
            Path = path;
            Reason = reason;
        }

        public string Path { get; private set; }
        public string Reason { get; private set; }
    }

    internal sealed class DownloadChoice
    {
        public DownloadChoice(bool latest, bool addon, string version, bool skipDownload)
        {
            Latest = latest;
            Addon = addon;
            Version = version;
            SkipDownload = skipDownload;
        }

        public bool Latest { get; private set; }
        public bool Addon { get; private set; }
        public string Version { get; private set; }
        public bool SkipDownload { get; private set; }
    }

    internal sealed class ReshadeDownload
    {
        public ReshadeDownload(string url, string version, bool addon)
        {
            Url = url;
            Version = version;
            Addon = addon;
        }

        public string Url { get; private set; }
        public string Version { get; private set; }
        public bool Addon { get; private set; }
    }

    internal sealed class KeySet
    {
        public KeySet(KeyBinding effects, KeyBinding menu, KeyBinding screenshot)
        {
            Effects = effects;
            Menu = menu;
            Screenshot = screenshot;
        }

        public KeyBinding Effects { get; private set; }
        public KeyBinding Menu { get; private set; }
        public KeyBinding Screenshot { get; private set; }
    }

    internal sealed class KeyBinding
    {
        public KeyBinding(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; private set; }
        public string Value { get; private set; }
    }

    internal sealed class IniDocument
    {
        private readonly List<string> lines;

        private IniDocument(IEnumerable<string> sourceLines)
        {
            lines = new List<string>(sourceLines);
        }

        public static IniDocument Load(string path)
        {
            return new IniDocument(File.ReadAllLines(path));
        }

        public void SetValue(string section, string key, string value)
        {
            int sectionIndex = FindSection(section);
            if (sectionIndex < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length != 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                return;
            }

            int insertIndex = lines.Count;
            for (int i = sectionIndex + 1; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (IsSectionHeader(trimmed))
                {
                    insertIndex = i;
                    break;
                }

                if (IsKeyLine(lines[i], key))
                {
                    lines[i] = key + "=" + value;
                    return;
                }
            }

            lines.Insert(insertIndex, key + "=" + value);
        }

        public void Save(string path)
        {
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        }

        private int FindSection(string section)
        {
            string expected = section.Trim();
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (!IsSectionHeader(trimmed))
                {
                    continue;
                }

                string name = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (name.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsSectionHeader(string trimmedLine)
        {
            return trimmedLine.Length >= 2 &&
                   trimmedLine[0] == '[' &&
                   trimmedLine[trimmedLine.Length - 1] == ']';
        }

        private static bool IsKeyLine(string line, string key)
        {
            int equalsIndex = line.IndexOf('=');
            if (equalsIndex < 0)
            {
                return false;
            }

            string foundKey = line.Substring(0, equalsIndex).Trim();
            return foundKey.Equals(key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
