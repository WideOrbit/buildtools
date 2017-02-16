// This is a script that is executed after the code is built, but before any tests
// are executed, and calculates *where* test assemblies should be tested.
// Some test assemblies are located in multiple folders and can only be executed
// successfully once.

// run with: csi.exe ExcludeTests.csx

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
    public static int Main(string[] args)
    {
        int result = 0;

        bool delete = args.Contains("-d");
        string[] argsWithoutFlags = args.Where(a => a != "-d").ToArray();

        if (argsWithoutFlags.Length != 1)
        {
            Log("Usage: csi.exe ExcludeTests.csx [-d] <folder>");
            result = 1;
        }
        else
        {
            string rootfolder = argsWithoutFlags[0];

            try
            {
                GetUniqueFolders(rootfolder, delete);
            }
            catch (ApplicationException ex)
            {
                LogColor(ex.Message, ConsoleColor.Red);
                result = 1;
            }
        }

        if (Environment.UserInteractive)
        {
            Log("Press any key to continue...");
            Console.ReadKey();
        }

        return result;
    }

    private static void GetUniqueFolders(string rootfolder, bool delete)
    {
        LogColor("***** Calculating unique paths *****", ConsoleColor.Cyan);

        Log($"Current Directory: '{Directory.GetCurrentDirectory()}'");

        string[] allfiles = Directory.GetFiles(".", "*Tests.dll", SearchOption.AllDirectories);

        //test
        //allfiles = File.ReadAllLines(@"tests.txt");

        Log($"Found {allfiles.Length} test assemblies.");

        allfiles = allfiles
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .ToArray();

        string[] excludetestassemblies = allfiles
            .Where(f =>
            {
                if (f.Contains(@"\obj\"))
                {
                    LogColor($"Ignoring (obj): '{f}'", ConsoleColor.DarkGray);
                    return false;
                }
                else
                {
                    return true;
                }
            })
            .ToArray()
            .GroupBy(f => Path.GetFileName(f))
            .Where(g =>
            {
                if (g.Count() > 1)
                {
                    return true;
                }
                else
                {
                    LogColor($"Ignoring (unique): '{g.Single()}'", ConsoleColor.DarkGray);
                    return false;
                }
            })
            .ToArray()
            .SelectMany(g => GetExcludesInGroup(g.ToArray()))
            .ToArray();

        if (delete)
        {
            foreach (string filename in excludetestassemblies)
            {
                Log($"Deleting: '{filename}'");
                File.Delete(filename);
            }
        }


        SetTCVariable("ExcludeAssemblies", string.Join(",", excludetestassemblies));
    }

    private static string[] GetExcludesInGroup(string[] filenames)
    {
        if (filenames.Length == 0)
        {
            LogColor("Empty set of test assemblues!", ConsoleColor.Red);
            return new string[] { };
        }

        if (filenames.Length == 1)
        {
            LogColor($"Keeping: '{filenames[0]}'", ConsoleColor.DarkGray);
            return new string[] { };
        }


        // Try to find a single path that matches the file name, based on strong naming convention
        string[] paths = filenames
            .Where(f => f.Split(Path.DirectorySeparatorChar).Any(ff => ff == Path.GetFileNameWithoutExtension(f)))
            .ToArray();

        if (paths.Length == 1)
        {
            LogColor($"Keeping: '{paths[0]}'", ConsoleColor.DarkGray);

            return filenames
                .Where(f => f != paths[0])
                .ToArray();
        }


        // Try to find a single path that matches the file name, based on weak naming convention
        paths = filenames
            .Where(f => f.Split(Path.DirectorySeparatorChar).Any(ff => Path.GetFileNameWithoutExtension(f).EndsWith(ff)))
            .ToArray();

        if (paths.Length == 1)
        {
            LogColor($"Keeping: '{paths[0]}'", ConsoleColor.DarkGray);

            return filenames
                .Where(f => f != paths[0])
                .ToArray();
        }


        LogColor($"Couldn't find any unique test assembly path, using first: '{filenames[0]}'", ConsoleColor.Red);

        return filenames
            .Skip(1)
            .ToArray();
    }

    private static void SetTCVariable(string variableName, string value)
    {
        Console.WriteLine($"##teamcity[setParameter name='{variableName}' value='{value}']");
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
