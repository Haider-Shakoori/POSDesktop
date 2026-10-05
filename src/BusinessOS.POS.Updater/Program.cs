using BusinessOS.POS.Updater;

if (args.Length != 2 || !string.Equals(args[0], "--apply", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: BusinessOS.POS.Updater --apply <plan.json>");
    return 2;
}

try
{
    var result = await UpdateApplier.ApplyAsync(args[1]);
    Console.WriteLine(result);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
