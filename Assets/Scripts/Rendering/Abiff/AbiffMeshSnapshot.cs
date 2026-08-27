using System;
using System.Collections.Generic;
using AODB.Common.DbClasses;
using AODB.Common.RDBObjects;
using UnityEngine;
using AoColor = AODB.Common.Structs.Color;
using AoVector2 = AODB.Common.Structs.Vector2;
using AoVector3 = AODB.Common.Structs.Vector3;
using AoQuaternion = AODB.Common.Structs.Quaternion;
using AoVertex = AODB.Common.Structs.Vertex;

public static class AbiffMeshSnapshot
{
    public static AbiffSubmeshSource[] FromRdbMesh(RDBMesh rdbMesh)
    {
        if (rdbMesh?.SubMeshes == null || rdbMesh.SubMeshes.Count == 0)
            return Array.Empty<AbiffSubmeshSource>();

        List<AbiffMaterialDesc> materials = ExtractMaterialDescs(rdbMesh);
        var submeshes = new AbiffSubmeshSource[rdbMesh.SubMeshes.Count];
        for (int s = 0; s < rdbMesh.SubMeshes.Count; s++)
        {
            RDBMesh_t.Submesh sub = rdbMesh.SubMeshes[s];
            AbiffMaterialDesc material = s < materials.Count
                ? materials[s]
                : FallbackMaterialFromAo(sub.Material);
            submeshes[s] = SnapshotSubmesh(sub, material);
        }

        return submeshes;
    }

    static List<AbiffMaterialDesc> ExtractMaterialDescs(RDBMesh rdbMesh)
    {
        var materials = new List<AbiffMaterialDesc>();
        RDBMesh_t mesh = rdbMesh.RDBMesh_t;
        if (mesh?.Members == null)
            return materials;

        foreach (RDBMesh_t.RTriMesh_t tri in mesh.GetMembers<RDBMesh_t.RTriMesh_t>())
        {
            if (tri.data < 0 || tri.data >= mesh.Members.Count)
                continue;
            if (mesh.Members[tri.data] is not RDBMesh_t.FAFTriMeshData_t data || data.mesh == null)
                continue;

            for (int i = 0; i < data.mesh.Length; i++)
            {
                int simpleIdx = data.mesh[i];
                if (simpleIdx < 0 || simpleIdx >= mesh.Members.Count ||
                    mesh.Members[simpleIdx] is not RDBMesh_t.SimpleMesh simple)
                {
                    materials.Add(AbiffMaterialDesc.CreateDefault());
                    continue;
                }

                materials.Add(BuildMaterialDesc(mesh, simple.material));
            }
        }

        return materials;
    }

    static AbiffMaterialDesc BuildMaterialDesc(RDBMesh_t mesh, int materialIndex)
    {
        AbiffMaterialDesc desc = AbiffMaterialDesc.CreateDefault();
        if (materialIndex < 0 || materialIndex >= mesh.Members.Count)
            return desc;

        string name;
        AoColor diff;
        AoColor emis;
        float shin;
        float opac;
        int deltaState;

        object member = mesh.Members[materialIndex];
        if (member is RDBMesh_t.FAFMaterial_t faf)
        {
            name = faf.name;
            diff = faf.diff;
            emis = faf.emis;
            shin = faf.shin;
            opac = faf.opac;
            deltaState = faf.delta_state;
        }
        else if (member is RDBMesh_t.DefaultMaterial_t def)
        {
            name = def.name;
            diff = def.diff;
            emis = def.emis;
            shin = def.shin;
            opac = def.opac;
            deltaState = def.delta_state;
        }
        else
        {
            return desc;
        }

        desc.Name = string.IsNullOrEmpty(name) ? "AbiffMat" : name;
        desc.Diffuse = new Color(diff.R, diff.G, diff.B, opac);
        desc.Emissive = new Color(emis.R, emis.G, emis.B, 1f);
        desc.Shininess = shin;
        desc.SpecularEnabled = true;

        if (deltaState < 0 || deltaState >= mesh.Members.Count)
            return desc;
        if (mesh.Members[deltaState] is not RDBMesh_t.RDeltaState delta)
            return desc;

        ApplyDeltaState(ref desc, mesh, delta);
        return desc;
    }

    static void ApplyDeltaState(ref AbiffMaterialDesc desc, RDBMesh_t mesh, RDBMesh_t.RDeltaState delta)
    {
        if (delta.rst_type != null && delta.rst_value != null)
        {
            int rstCount = (int)Math.Min(delta.rst_count, Math.Min(delta.rst_type.Length, delta.rst_value.Length));
            for (int i = 0; i < rstCount; i++)
            {
                switch ((D3DRenderStateType)delta.rst_type[i])
                {
                    case D3DRenderStateType.D3DRS_CULLMODE:
                        desc.TwoSided = true;
                        break;
                    case D3DRenderStateType.D3DRS_ALPHABLENDENABLE:
                        desc.ApplyAlpha = delta.rst_value[i] == 1;
                        break;
                    case D3DRenderStateType.D3DRS_SPECULARENABLE:
                        if (delta.rst_value[i] == 0)
                            desc.SpecularEnabled = false;
                        break;
                }
            }
        }

        if (delta.tch_type == null || delta.tch_text == null)
            return;

        int tchCount = (int)Math.Min(delta.tch_count, Math.Min(delta.tch_type.Length, delta.tch_text.Length));
        for (int i = 0; i < tchCount; i++)
        {
            int texIdx = delta.tch_text[i];
            if (texIdx < 0 || texIdx >= mesh.Members.Count)
                continue;
            if (mesh.Members[texIdx] is not RDBMesh_t.FAFTexture_t texture)
                continue;
            if (texture.creator < 0 || texture.creator >= mesh.Members.Count)
                continue;
            if (mesh.Members[texture.creator] is not RDBMesh_t.AnarchyTexCreator_t creator)
                continue;

            int texId = (int)creator.inst;
            switch ((TextureChannelType)delta.tch_type[i])
            {
                case TextureChannelType.Diffuse:
                    desc.DiffuseTextureId = texId;
                    break;
                case TextureChannelType.Emissive:
                    desc.EmissionTextureId = texId;
                    break;
            }
        }
    }

    static AbiffMaterialDesc FallbackMaterialFromAo(AODB.Common.Structs.AOMaterial aoMaterial)
    {
        AbiffMaterialDesc desc = AbiffMaterialDesc.CreateDefault();
        if (aoMaterial == null)
            return desc;

        desc.Name = string.IsNullOrEmpty(aoMaterial.MaterialName) ? "AbiffMat" : aoMaterial.MaterialName;
        desc.DiffuseTextureId = (int)aoMaterial.Texture;
        return desc;
    }

    static AbiffSubmeshSource SnapshotSubmesh(RDBMesh_t.Submesh sub, AbiffMaterialDesc material)
    {
        AoVertex[] verts = sub.Vertices ?? Array.Empty<AoVertex>();
        var positions = new Vector3[verts.Length];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            AoVector3 pos = verts[i].Position;
            AoVector3 nrm = verts[i].Normal;
            AoVector2 uv = verts[i].UVs;
            positions[i] = new Vector3(pos.X, pos.Y, pos.Z);
            normals[i] = new Vector3(nrm.X, nrm.Y, nrm.Z);
            uvs[i] = new Vector2(uv.X, uv.Y);
        }

        int[] triangles = sub.Triangles != null
            ? (int[])sub.Triangles.Clone()
            : Array.Empty<int>();

        AoVector3 basePos = sub.BasePos;
        AoQuaternion baseRot = sub.BaseRotation;

        // Prefer FAF-derived diffuse id; fall back to AODB's simplified AOMaterial.Texture.
        if (material.DiffuseTextureId <= 0 && sub.Material != null && sub.Material.Texture > 0)
            material = material.WithDiffuseTexture((int)sub.Material.Texture);

        return new AbiffSubmeshSource
        {
            Positions = positions,
            Normals = normals,
            UVs = uvs,
            Triangles = triangles,
            BasePosition = new Vector3(basePos.X, basePos.Y, basePos.Z),
            BaseRotation = new Quaternion(baseRot.X, baseRot.Y, baseRot.Z, baseRot.W),
            Material = material
        };
    }

    enum TextureChannelType
    {
        Diffuse = 0,
        Emissive = 1
    }

    enum D3DRenderStateType
    {
        D3DRS_CULLMODE = 22,
        D3DRS_ALPHABLENDENABLE = 27,
        D3DRS_SPECULARENABLE = 29
    }
}
