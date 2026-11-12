using System;
using PristonTale;
using PristonTale.Display;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;

if (args.Length == 2 && args[0] == "--validate-display")
{
    // The game is disposed on the thread that ran it (SDL and Vulkan objects belong to it) before the report is awaited.
    var probe = new DisplayValidationProbe(args[1]);
    using (probe)
    {
        probe.Run();
    }

    await probe.WriteReportAsync();
    return 0;
}

if (!ClientLaunchOptions.TryParse(args, out var options, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(ClientLaunchOptions.Usage);
    return 1;
}

using var log = ClientLogFile.Open(AppContext.BaseDirectory);
using var game = new Game1(options);
game.Run();
return 0;
