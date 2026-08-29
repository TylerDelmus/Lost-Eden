using System;
using System.Text;
using AODB;
using AODB.Common.RDBObjects;

class Program {
  static void Main(string[] args) {
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    using var rdb = new RdbController(args[0]);
    int inst = (4582 << 16) | 720;
    var s = rdb.Get<SurfaceResource>(inst);
    Console.WriteLine($"Get by instance: surfaces={s.Surfaces.Count} footer={s.FooterOk} typeAttr ok");
    var s2 = rdb.Get<SurfaceResource>(ResourceTypeId.SurfaceResource, inst);
    Console.WriteLine($"Get by type: surfaces={s2.Surfaces.Count}");
  }
}
