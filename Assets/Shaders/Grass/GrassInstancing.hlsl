#ifndef AOREBIRTH_GRASS_INSTANCING_INCLUDED
#define AOREBIRTH_GRASS_INSTANCING_INCLUDED

// ---------------------------------------------------------------------------
// Per-instance grass transforms for Shader Graph, Unity 6 style.
//
// NOTE: there are deliberately NO #pragma directives in this file, no Setup()
// function, and no reference to unity_ObjectToWorld. That older approach -
// `#pragma instancing_options procedural:Setup` writing unity_ObjectToWorld -
// does not work in an HDRP Shader Graph and produces exactly two errors:
//
//   "UNITY_INSTANCING_PROCEDURAL_FUNC must be defined"   (UnityInstancing.hlsl)
//       Shader Graph includes UnityInstancing.hlsl in the pass template long
//       before it includes a Custom Function file, so the define arrives too
//       late for the guard that checks it.
//
//   undeclared identifier 'Use_Macro_UNITY_MATRIX_M_instead_of_unity_ObjectToWorld'
//       HDRP deliberately #defines unity_ObjectToWorld to that poison value to
//       force everything through UNITY_MATRIX_M - which is a const expression
//       and cannot be assigned to. So there is nothing to write the matrix into.
//
// From Unity 6 the Instance ID node declares SV_InstanceID itself, so the index
// can simply be handed in as a node input and the transform done by hand.
// Graphics.RenderMeshIndirect draws with an identity object-to-world, so object
// space IS world space here: writing an absolute world position into the Vertex
// Position block is correct, and HDRP's camera-relative offset is still applied
// afterwards by UNITY_MATRIX_M.
// ---------------------------------------------------------------------------

#ifndef SHADERGRAPH_PREVIEW
// One float4x4 per blade/clump, baked by PlayfieldGrassBuilder and bound with
// Material.SetBuffer.
StructuredBuffer<float4x4> _GrassInstances;
#endif

// Globals, set once per playfield with Shader.SetGlobalVector - identical for
// every chunk. NOT Shader Graph blackboard properties: declaring them in both
// places is a redefinition error.
float4 _GrassWindParams; // xy = wind direction (world XZ, normalised), z = strength (metres), w = frequency
float4 _GrassFadeParams; // x = cull distance, y = fade band width, z = blade height in mesh units
float4 _GrassWindParams2; // x = per-instance phase scale, y = gust amplitude, zw = unused

// --- Vertex stage ---------------------------------------------------------
// Looks up this instance's transform, applies wind, and returns the result in
// the space the Vertex Position / Normal blocks expect.
void GrassInstance_float(
    float3 positionOS,
    float3 normalOS,
    float instanceID,
    out float3 PositionOut,
    out float3 NormalOut)
{
#ifdef SHADERGRAPH_PREVIEW
    PositionOut = positionOS;
    NormalOut = normalOS;
#else
    float4x4 m = _GrassInstances[(uint) instanceID];

    // Blade base in world space, and the instance's uniform scale - wind
    // amplitude has to scale with the blade or big and small blades bend by
    // different visual amounts.
    float3 pivotWS = float3(m._m03, m._m13, m._m23);
    float instanceScale = length(float3(m._m00, m._m10, m._m20));

    // 0 at the base, 1 at the tip, squared so the root stays planted.
    float bend = saturate(positionOS.y / max(_GrassFadeParams.z, 1e-4f));
    bend *= bend;

    float phase = dot(pivotWS.xz, _GrassWindParams.xy) * _GrassWindParams2.x;
    float t = _Time.y * _GrassWindParams.w;

    float sway = sin(t + phase);
    // Slower second harmonic so the field does not read as one sine wave.
    float gust = sin(t * 0.37f + phase * 0.63f) * _GrassWindParams2.y;

    float amount = (sway + gust) * _GrassWindParams.z * bend * instanceScale;

    // Absolute world position of this vertex. Wind is added here, after the
    // transform, so every blade leans the same way regardless of its random yaw.
    float3 positionAWS = mul(m, float4(positionOS, 1.0f)).xyz;
    positionAWS += float3(_GrassWindParams.x, 0.0f, _GrassWindParams.y) * amount;

    float3 normalAWS = normalize(mul((float3x3) m, normalOS));

    // Round-trip back through the pipeline's own matrices instead of assuming the
    // draw's object-to-world is identity.
    //
    // HDRP renders camera-relative: world space in the shader is offset by the camera
    // position. Whether that offset is already folded into unity_ObjectToWorld depends
    // on the draw path, and for an indirect draw there is no per-object data for it to
    // be folded into - so feeding an absolute world position straight to the Position
    // block displaces everything by the camera position, which is what put the grass up
    // in the sky. Converting to camera-relative and then back through the inverse model
    // matrix is correct either way: if M is identity the two steps leave the
    // camera-relative position, and if M already carries the offset they cancel.
    PositionOut = TransformWorldToObject(GetCameraRelativePositionWS(positionAWS));
    NormalOut = TransformWorldToObjectDir(normalAWS, true);
#endif
}

// --- Fragment stage -------------------------------------------------------
// 1 up close, ramping to 0 across the band that ends at the cull distance, so
// blades thin out instead of popping when the chunk stops being drawn. Feed it
// Position (Absolute World) and the Camera node's Position - HDRP's plain
// "World" position is camera-relative and would give a fade stuck at 1.
void GrassFade_float(float3 positionWS, float3 cameraPositionWS, out float Out)
{
    Out = 1.0f;

#ifndef SHADERGRAPH_PREVIEW
    float cullDistance = _GrassFadeParams.x;
    float fadeBand = max(_GrassFadeParams.y, 1e-4f);
    float d = distance(positionWS, cameraPositionWS);
    Out = 1.0f - saturate((d - (cullDistance - fadeBand)) / fadeBand);
#endif
}

#endif // AOREBIRTH_GRASS_INSTANCING_INCLUDED