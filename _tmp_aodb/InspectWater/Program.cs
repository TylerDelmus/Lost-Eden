using System;
using System.Linq;
using System.Reflection;
using AODB.Common.RDBObjects;

class Program {
  static void Main() {
    var t = typeof(RDBPlayfield);
    foreach (var a in t.GetCustomAttributes(false)) Console.WriteLine(a);
    // try find ResourceTypeId attribute field
    var attrType = t.Assembly.GetTypes().FirstOrDefault(x => x.Name == "RDBRecordAttribute");
    if (attrType != null) {
      var attr = t.GetCustomAttributes(attrType, false).FirstOrDefault();
      if (attr != null) {
        foreach (var p in attrType.GetProperties()) Console.WriteLine(p.Name + "=" + p.GetValue(attr));
        foreach (var f in attrType.GetFields()) Console.WriteLine(f.Name + "=" + f.GetValue(attr));
      }
    }
  }
}
