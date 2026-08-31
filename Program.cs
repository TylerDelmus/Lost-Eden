using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

var paths = Directory.GetFiles(Path.GetDirectoryName(args[2])!, "*.dll").Concat(new[]{args[0],args[1],args[2]}).Distinct().ToArray();
var resolver = new PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(resolver);
var asm = mlc.LoadFromAssemblyPath(args[0]);
// CharacterActionType enum values related to fight
var t = asm.GetType("SmokeLounge.AOtomation.Messaging.GameData.CharacterActionType")
    ?? asm.GetType("AOSharp.Common.GameData.CharacterActionType");
Console.WriteLine("CharacterActionType: " + (t?.FullName ?? "null"));
if (t != null && t.IsEnum)
{
  foreach (var name in Enum.GetNames(t))
  {
    if (name.IndexOf("Fight", StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("Attack", StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("Combat", StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("Target", StringComparison.OrdinalIgnoreCase)>=0)
      Console.WriteLine($"  {name} = {(int)Enum.Parse(t, name)}");
  }
}
var t2 = asm.GetType("SmokeLounge.AOtomation.Messaging.Messages.N3Messages.CharacterActionMessage");
if (t2 != null)
  foreach (var p in t2.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.FlattenHierarchy))
    Console.WriteLine($"CA prop {p.PropertyType.Name} {p.Name}");
