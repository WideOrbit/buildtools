// This is a script that is used to compress the output of each projects to a zip file,
// aka an artifact.

// run with: csi.exe CompressApps.csx

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

public class Program
{
    static string toolsfolder = @"..\Tools";

    public static int Main(string[] args)
    {
        int result = 0;

        if (args.Length != 1)
        {
            Log("Usage: csi.exe CompressApps.csx <folder>");
            result = 1;
        }
        else
        {
            string rootfolder = args[0];

            string currentdir = null;
            try
            {
                currentdir = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(rootfolder);

                CompressFolders(rootfolder);
            }
            catch (ApplicationException ex)
            {
                LogColor(ex.Message, ConsoleColor.Red);
                result = 1;
            }
            finally
            {
                if (currentdir != null)
                {
                    Directory.SetCurrentDirectory(currentdir);
                }
            }
        }

        if (Environment.UserInteractive)
        {
            Log("Press any key to continue...");
            Console.ReadKey();
        }

        return result;
    }

    private static void CompressFolders(string rootfolder)
    {
        LogColor("***** Compressing apps *****", ConsoleColor.Cyan);

        Log($"Current Directory: '{Directory.GetCurrentDirectory()}'");

        string[] folders = Directory.GetDirectories(".")
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .ToArray();

        Log($"Cores: {Environment.ProcessorCount}");

        IEnumerable<string> results = folders
            .AsParallel()
            .Select(folder =>
            {
                DateTime t1 = DateTime.UtcNow;

                string zipFile = $"{folder}.zip";

                Log($"Zipping: '{zipFile}'");

                if (File.Exists(zipFile))
                {
                    Log($"Deleting: '{zipFile}'");
                    File.Delete(zipFile);
                }

                string output, error;
                string zipexe = Path.Combine(toolsfolder, "7z.exe");
                int result = RunCommand(zipexe, $"a \"{zipFile}\" \"{folder}\"", true, out output, out error);
                if (result > 0)
                {
                    string consoletext =
                        "***** output *****" + Environment.NewLine +
                        output + Environment.NewLine +
                        "***** error *****" + Environment.NewLine +
                        error;

                    return consoletext;
                }

                DeleteFolder(folder);

                DateTime t2 = DateTime.UtcNow;
                Log($"Zipped: '{zipFile}': {t1.ToString()}->{t2.ToString()}: {(t2 - t1).ToString()}");
                return null;
            });

        string[] errors = results
            .Where(r => r != null)
            .OrderBy(r => r)
            .ToArray();
        if (errors.Length > 0)
        {
            foreach (string error in errors)
            {
                LogColor(error, ConsoleColor.Red);
            }
            throw new ApplicationException("Couldn't create zip file.");
        }
    }

    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Log($"Couldn't find folder: '{folder}'");
            return;
        }

        Log($"Deleting folder: '{folder}'");
        try
        {
            Directory.Delete(folder, true);
        }
        catch (System.Exception ex)
        {
            Log($"Exception: {ex.ToString()}");

            string regpath = @"HKEY_CURRENT_USER\Software\Sysinternals\Handle";

            Log($"Setting reg value: '{regpath}" + @"\EulaAccepted'");
            Registry.SetValue(regpath, "EulaAccepted", 1, RegistryValueKind.DWord);

            string handleexe = Path.Combine(toolsfolder, "handle.exe");
            if (File.Exists(handleexe))
            {
                string localfolder = Path.GetFileName(folder);

                Log($"Running handle tool for folder: '{folder}'");
                string output, error;
                RunCommand(handleexe, $"-u {localfolder}", false, out output, out error);
            }
            else
            {
                LogColor($"Couldn't find handle tool: '{handleexe}'", ConsoleColor.Yellow);
            }
        }
    }

    private static int RunCommand(string exefile, string args, bool redirect, out string output, out string error)
    {
        Process process = new Process();
        process.StartInfo = new ProcessStartInfo(exefile, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect
        };

        //LogColor($"Running: >>{exefile}<< >>{args}<<", ConsoleColor.DarkGray);

        process.Start();
        if (redirect)
        {
            output = process.StandardOutput.ReadToEnd();
            error = process.StandardError.ReadToEnd();
        }
        else
        {
            output = null;
            error = null;
        }
        process.WaitForExit();

        //LogColor($"Ran: >>{exefile}<< >>{args}<< " + output.Length + " " + error.Length, ConsoleColor.DarkGray);

        return process.ExitCode;
    }

    private static void LogColor(string message, ConsoleColor color)
    {
        ConsoleColor oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Log(message);
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }
    }

    private static void Log(string message)
    {
        string hostname = Dns.GetHostName();
        Console.WriteLine($"{hostname}: {message}");
    }
}

return Program.Main(Environment.GetCommandLineArgs().Skip(2).ToArray());
