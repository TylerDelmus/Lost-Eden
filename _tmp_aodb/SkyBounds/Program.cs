using System.Text;
using AODB;
using AODB.Common.RDBObjects;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string ao = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Steam\steamapps\common\Anarchy Online";
using var rdb = new RdbController(ao);
foreach (int id in new[] { 1439, 1442, 204393, 227651 })
{
    RDBMesh mesh;
    try { mesh = rdb.Get<RDBMesh>(id); } catch (Exception ex) { Console.WriteLine($"id={id} err={ex.Message}"); continue; }
    if (mesh?.SubMeshes == null) { Console.WriteLine($"id={id} null"); continue; }
    float minx=1e9f,miny=1e9f,minz=1e9f,maxx=-1e9f,maxy=-1e9f,maxz=-1e9f;
    int verts=0;
    foreach (var sm in mesh.SubMeshes)
    {
        if (sm?.Vertices == null) continue;
        foreach (var v in sm.Vertices)
        {
            verts++;
            var p = v.Position;
            if (p.X < minx) minx=p.X; if (p.X > maxx) maxx=p.X;
            if (p.Y < miny) miny=p.Y; if (p.Y > maxy) maxy=p.Y;
            if (p.Z < minz) minz=p.Z; if (p.Z > maxz) maxz=p.Z;
        }
    }
    Console.WriteLine($"id={id} verts={verts} bounds=({minx:F3},{miny:F3},{minz:F3})-({maxx:F3},{maxy:F3},{maxz:F3}) ext=({maxx-minx:F3},{maxy-miny:F3},{maxz-minz:F3})");
}
