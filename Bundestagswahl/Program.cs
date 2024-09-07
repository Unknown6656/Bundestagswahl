using Bundestagswahl;
using System;

using Unknown6656.Runtime.Console;



using Renderer renderer = new()
{
    CurrentRenderSize = RenderSize.Large
};
await using ConsoleResizeListener resize = new();

resize.SizeChanged += (_, _, _, _) =>
{
    // fix rendering of modal form during resizing.

    try
    {
        renderer.Render(true);
    }
    catch
    {
        renderer.Render(true); // do smth. if it fails the second time
    }
};
resize.Start();

await renderer.FetchPollsAsync();

while (Console.ReadKey(true) is { Key: not ConsoleKey.Escape } key)
    renderer.HandleInput(key);

resize.Stop();

Console.CursorTop = Console.WindowHeight - 1;
Console.CursorLeft = Console.WindowWidth - 1;
Console.WriteLine();
Console.ResetColor();
