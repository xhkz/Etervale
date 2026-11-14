using PristonTale.HudUpscale;

const string Usage = """
    pthud prepare <assetRoot> <work> [--set hud|world] [folder ...]
    pthud run <realesrgan-ncnn-vulkan> <work> [--model <name>]
    pthud collect <assetRoot> <work> [--set hud|world]
    pthud upscale <assetRoot> <work> <realesrgan-ncnn-vulkan> [--set hud|world] [--model <name>] [folder ...]

    hud (the default): interface images under image, without image/Sinimage/Items, copied at 2x, 3x and 4x.
    world: field textures under Field, padded with their opposite edges, copied at 2x.
    folders are asset-relative and replace the set's own; the model defaults to realesrgan-x4plus.
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

var target = HudUpscaler.Hud;
var setAt = rest.IndexOf("--set");
if (setAt >= 0)
{
    if (setAt + 1 >= rest.Count || HudUpscaler.TargetNamed(rest[setAt + 1]) is not { } named)
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    target = named;
    rest.RemoveRange(setAt, 2);
}

switch (args[0])
{
    case "prepare":
        HudUpscaler.Prepare(target, args[1], args[2], rest.Count > 0 ? rest : target.Folders, Console.Out);
        return 0;
    case "run":
        return HudUpscaler.Run(args[1], args[2], model, Console.Out);
    case "collect":
        return HudUpscaler.Collect(target, args[1], args[2], Console.Out) == 0 ? 0 : 1;
    case "upscale" when rest.Count >= 1:
        var esrgan = rest[0];
        rest.RemoveAt(0);
        HudUpscaler.Prepare(target, args[1], args[2], rest.Count > 0 ? rest : target.Folders, Console.Out);
        var exit = HudUpscaler.Run(esrgan, args[2], model, Console.Out);
        if (exit != 0)
        {
            Console.Error.WriteLine($"the upscaler exited with {exit}");
            return exit;
        }

        return HudUpscaler.Collect(target, args[1], args[2], Console.Out) == 0 ? 0 : 1;
    default:
        Console.Error.WriteLine(Usage);
        return 2;
}
