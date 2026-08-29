using System;
using System.Linq;
using System.Reflection;
using AODB.Common.RDBObjects;

class P {
  static void Main() {
    Dump(typeof(RDBPlayfield));
    var zone = typeof(RDBPlayfield).GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic)
      .Concat(typeof(RDBPlayfield).Assembly.GetTypes().Where(t => t.Name == "Zone" || t.FullName.Contains("RDBPlayfield")))
      .Distinct();
    foreach (var t in zone) {
      Console.WriteLine("TYPE " + t.FullName);
      Dump(t);
    }
    Console.WriteLine("=== ResourceTypeId ===");
    foreach (var name in Enum.GetNames(typeof(ResourceTypeId))) {
      int v = (int)Enum.Parse(typeof(ResourceTypeId), name);
      if (v >= 1000000 && v <= 1000020) Console.WriteLine(name + " = " + v);
    }
    Console.WriteLine("=== Tilemap props ===");
    Dump(typeof(Tilemap));
    Console.WriteLine("=== RdbController methods with GetRaw/Get ===");
    var rdb = typeof(AODB.RdbController);
    foreach (var m in rdb.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
      Console.WriteLine(m);
  }
  static void Dump(Type t) {
    Console.WriteLine("--- " + t.FullName + " ---");
    foreach (var p in t.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
      Console.WriteLine("  P " + p.PropertyType.Name + " " + p.Name);
    foreach (var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
      Console.WriteLine("  F " + f.FieldType.Name + " " + f.Name);
  }
}
