global using ConsoleColor = Unknown6656.Console.ConsoleColor;
global using Console = Unknown6656.Console.Console;

using Unknown6656.Console;

using Bundestagswahl;


Console.CursorVisible = false;
Console.CancelKeyPress += (_, evt) =>
{
    Console.ResetGraphicRenditions();
    Console.SetCursorPosition(0, Console.WindowHeight - 1);
    Console.CursorVisible = true;

    evt.Cancel = false;
};

await using IPollDatabase poll_db = new BinaryPollDatabase(new("poll-cache.bin"));
using Renderer renderer = new(poll_db)
{
    CurrentRenderSize = RenderSize.Large
};
await using ConsoleResizeListener resize = new();

resize.SizeChanged += (_, _, _, _) => renderer.Render(true);
resize.Start();

await renderer.Run();

resize.Stop();

Console.SetCursorPosition(Console.WindowWidth - 1, Console.WindowHeight - 1);
Console.WriteLine();
Console.ResetColor();
