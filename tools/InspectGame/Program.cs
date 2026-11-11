using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: InspectGame <Assembly-CSharp.dll> <output-directory> <type> [...]");
    return 1;
}
Directory.CreateDirectory(args[1]);
var decompiler = new CSharpDecompiler(args[0], new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
foreach (var type in args.Skip(2))
{
    File.WriteAllText(Path.Combine(args[1], type.Replace('.', '_') + ".cs"), decompiler.DecompileTypeAsString(new FullTypeName(type)));
    Console.WriteLine(type);
}
return 0;
