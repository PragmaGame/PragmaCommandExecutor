// Compares a player assembly with its stripped copy: dotnet run --file strip-check.cs -- <original.dll> <stripped.dll> [max lines]
// Needs the .NET 10 SDK (file-based apps). Lists types, methods and fields the linker removed; compiler-generated
// members other than auto-property backing fields are skipped.
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

static HashSet<string> Members(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    var result = new HashSet<string>();
    foreach (var handle in md.TypeDefinitions)
    {
        var type = md.GetTypeDefinition(handle);
        var name = md.GetString(type.Namespace) + "." + md.GetString(type.Name);
        if (!type.GetDeclaringType().IsNil)
        {
            name = md.GetString(md.GetTypeDefinition(type.GetDeclaringType()).Name) + "/" + md.GetString(type.Name);
        }
        result.Add(name);
        foreach (var methodHandle in type.GetMethods())
        {
            result.Add(name + "::" + md.GetString(md.GetMethodDefinition(methodHandle).Name));
        }
        foreach (var fieldHandle in type.GetFields())
        {
            result.Add(name + "." + md.GetString(md.GetFieldDefinition(fieldHandle).Name));
        }
    }
    return result;
}

var original = Members(args[0]);
var stripped = Members(args[1]);
var missing = original.Except(stripped).Where(m => !m.Contains("<") || m.Contains("k__BackingField")).OrderBy(m => m).ToList();
Console.WriteLine($"{Path.GetFileName(args[0])}: {original.Count} members, stripped keeps {stripped.Count}, missing {missing.Count}");
foreach (var m in missing.Take(int.Parse(args.Length > 2 ? args[2] : "40")))
{
    Console.WriteLine("  - " + m);
}
