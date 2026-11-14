using PristonTale.HudUpscale;

const string Usage = """
    pthud prepare <assetRoot> <work> [folder ...]
    pthud run <realesrgan-ncnn-vulkan> <work> [--model <name>]
    pthud collect <assetRoot> <work>
    pthud upscale <assetRoot> <work> <realesrgan-ncnn-vulkan> [--model <name>] [folder ...]

    folders are asset-relative (default: image, without image/Sinimage/Items); the model defaults to realesrgan-x4plus.
    """;

if (args.Length < 3)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var rest = args.Skip(3).ToList();
var model = HudUpscaler.DefaultModel;
var modelAt = rest.IndexOf("--model");
if (modelAt >= 0)
{
    if (modelAt + 1 >= rest.Count)
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    model = rest[modelAt + 1];
    rest.RemoveRange(modelAt, 2);
}

switch (args[0])
{
    case "prepare":
        HudUpscaler.Prepare(args[1], args[2], rest.Count > 0 ? rest : HudUpscaler.DefaultFolders, Console.Out);
        return 0;
    case "run":
        return HudUpscaler.Run(args[1], args[2], model, Console.Out);
    case "collect":
        return HudUpscaler.Collect(args[1], args[2], Console.Out) == 0 ? 0 : 1;
    case "upscale" when rest.Count >= 1:
        var esrgan = rest[0];
        rest.RemoveAt(0);
        HudUpscaler.Prepare(args[1], args[2], rest.Count > 0 ? rest : HudUpscaler.DefaultFolders, Console.Out);
        var exit = HudUpscaler.Run(esrgan, args[2], model, Console.Out);
        if (exit != 0)
        {
            Console.Error.WriteLine($"the upscaler exited with {exit}");
            return exit;
        }

        return HudUpscaler.Collect(args[1], args[2], Console.Out) == 0 ? 0 : 1;
    default:
        Console.Error.WriteLine(Usage);
        return 2;
}
