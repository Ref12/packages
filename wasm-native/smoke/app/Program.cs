using System;
using System.Runtime.InteropServices.JavaScript;

Console.WriteLine("hello from main");

public static partial class Interop
{
    [JSExport]
    public static int Add(int a, int b) => a + b;

    [JSImport("globalThis.console.log")]
    public static partial void Log(string s);
}
