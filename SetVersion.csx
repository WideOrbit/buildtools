using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class Program
{
    public static int Main(string[] args)
    {
        string branchname;
        Dictionary<string, string> tcprops = GetTeamcityVariables();

        if (tcprops.ContainsKey("teamcity.build.branch"))
        {
            branchname = tcprops["teamcity.build.branch"];
            Log($"Using teamcity.build.branch: '{branchname}'");
        }
        else if (tcprops.ContainsKey("vcsroot.branch"))
        {
            branchname = tcprops["vcsroot.branch"];
            Log($"Using vcsroot.branch: '{branchname}'");
        }
        else
        {
            LogColor("Couldn't find any branch, using '1'", ConsoleColor.Yellow);
            branchname = "1";
        }
        Log($"branchname: '{branchname}'");

        int index = branchname.LastIndexOf("/");
        if (index != -1)
        {
            branchname = branchname.Substring(index + 1);
        }
        if (branchname.EndsWith(".x") || branchname.EndsWith(".X"))
        {
            branchname = branchname.Substring(0, branchname.Length - 2);
        }
        if (!branchname.ToCharArray().All(c => char.IsDigit(c) || c == '.'))
        {
            branchname = "1";
        }

        Log($"branchname: '{branchname}'");

        string buildnumber = Environment.GetEnvironmentVariable("BUILD_NUMBER");
        Log("buildnumber: " + ((buildnumber == null) ? "<null>" : $"'{buildnumber}'"));
        if (string.IsNullOrEmpty(buildnumber))
        {
            LogColor("Couldn't find any buildnumber, using '0'", ConsoleColor.Yellow);
            buildnumber = "0";
        }
        buildnumber = $"{branchname}.{buildnumber}";
        Log($"buildnumber: '{buildnumber}'");

        Log($"##teamcity[buildNumber '{buildnumber}']");

        return 0;
    }

    private static Dictionary<string, string> GetTeamcityVariables()
    {
        Dictionary<string, string> empty = new Dictionary<string, string>();

        string buildpropfile = Environment.GetEnvironmentVariable("TEAMCITY_BUILD_PROPERTIES_FILE");
        if (string.IsNullOrEmpty(buildpropfile))
        {
            LogColor("Couldn't find Teamcity build properties file.", ConsoleColor.Yellow);
            return empty;
        }
        if (!File.Exists(buildpropfile))
        {
            LogColor($"Couldn't find Teamcity build properties file: '{buildpropfile}'", ConsoleColor.Yellow);
            return empty;
        }

        Log($"Reading Teamcity build properties file: '{buildpropfile}'");
        string[] rows = File.ReadAllLines(buildpropfile);

        var valuesBuild = GetPropValues(rows);

        string configpropfile = valuesBuild["teamcity.configuration.properties.file"];
        if (string.IsNullOrEmpty(configpropfile))
        {
            LogColor("Couldn't find Teamcity config properties file.", ConsoleColor.Yellow);
            return empty;
        }
        if (!File.Exists(configpropfile))
        {
            LogColor($"Couldn't find Teamcity config properties file: '{configpropfile}'", ConsoleColor.Yellow);
            return empty;
        }

        Log($"Reading Teamcity config properties file: '{configpropfile}'");
        rows = File.ReadAllLines(configpropfile);

        var valuesConfig = GetPropValues(rows);

        return valuesConfig;
    }

    private static Dictionary<string, string> GetPropValues(string[] rows)
    {
        Dictionary<string, string> dic = new Dictionary<string, string>();

        foreach (string row in rows)
        {
            int index = row.IndexOf('=');
            if (index != -1)
            {
                string key = row.Substring(0, index);
                string value = Regex.Unescape(row.Substring(index + 1));
                dic[key] = value;
            }
        }

        return dic;
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
    }

    private static void LogColor(string message, ConsoleColor color)
    {
        ConsoleColor oldcolor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = oldcolor;
        }
    }
}

return Program.Main(Environment.GetCommandLineArgs().Skip(2).ToArray());
