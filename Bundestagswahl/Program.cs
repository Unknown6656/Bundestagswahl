global using Console = Unknown6656.Console.Console;
global using ConsoleColor = Unknown6656.Console.ConsoleColor;

using System;

using Unknown6656.Console;

using Bundestagswahl;



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

Console.SetCursorPosition(Console.WindowWidth - 1, Console.WindowHeight - 1);
Console.WriteLine();
Console.ResetColor();
