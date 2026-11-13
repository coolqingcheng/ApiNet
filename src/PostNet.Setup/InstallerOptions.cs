namespace PostNet.Setup;

public sealed record InstallerOptions(string InstallDirectory, string LogPath, bool StartApplication, bool Help, IReadOnlyList<string> ForwardedArguments)
{
    public static InstallerOptions Parse(string[] args, string defaultDirectory, string defaultLog)
    {
        var directory = defaultDirectory;
        var log = defaultLog;
        var start = true;
        var help = false;
        List<string> forwarded = [];
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument == "--") { forwarded.AddRange(args[i..]); break; }
            if (argument == "--no-start") { start = false; continue; }
            if (argument is "--silent" or "-s") { start = false; continue; }
            if (argument is "--help" or "-h" or "--version" or "-V") help = true;
            if (argument is "--installto" or "-t" or "--log" or "-l")
            {
                if (++i >= args.Length) throw new ArgumentException($"{argument} 缺少参数。");
                if (argument is "--installto" or "-t") directory = args[i]; else log = args[i];
                continue;
            }
            if (argument.StartsWith("--installto=", StringComparison.Ordinal)) { directory = argument[12..]; continue; }
            if (argument.StartsWith("--log=", StringComparison.Ordinal)) { log = argument[6..]; continue; }
            forwarded.Add(argument);
        }
        return new(Path.GetFullPath(directory), Path.GetFullPath(log), start, help, forwarded);
    }
}
