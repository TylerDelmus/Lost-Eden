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

// Per-instance ground tint, RGBA8 packed into one word: bits 0-7 red, 8-15 green,
// 16-23 blue. Baked by PlayfieldGrassBuilder from the colour of the ground texture each
// blade stands on, so pale or dead-looking patches of terrain grow pale grass.
StructuredBuffer<uint> _GrassTints;
#endif

// Globals, set once per playfield with Shader.SetGlobalVector - identical for
// every chunk. NOT Shader Graph blackboard properties: declaring them in both
// places is a redefinition error.
float4 _GrassWindParams; // xy = wind direction (world XZ, normalised), z = strength (metres), w = frequency
float4 _GrassFadeParams; // x = cull distance, y = fade band width, z = blade height in mesh units
float4 _GrassWindParams2; // x = world phase scale, y = gust amplitude, z = per-instance phase jitter, w = unused
float4 _GrassVariantParams; // x = slices in the array, y = normal up-blend, z = ground tint blend

// Deliberately not HDRP's SRGBToLinear: this file is included into a Shader Graph pass and
// should not depend on which HDRP headers happen to be in scope. The gamma 2.2 approximation
// is well within 8-bit tint precision.
float3 GrassSRGBToLinear(float3 c)
{
    return pow(max(c, 0.0f), 2.2f);
}

// Dave Hoskins' hash13 - a stable 0..1 value from a world position, with no
// trig. sin-based hashes band badly once world coordinates get into the
// thousands, which playfield coordinates do.
float GrassHash13(float3 p3)
{
    p3 = frac(p3 * 0.1031f);
    p3 += dot(p3, p3.zyx + 31.32f);
    return frac((p3.x + p3.y) * p3.z);
}

// --- Vertex stage ---------------------------------------------------------
// Looks up this instance's transform, applies wind, and returns the result in
// the space the Vertex Position / Normal blocks expect. Variation is a stable
// per-instance 0..1 value for the fragment stage to tint with - route it through
// a Custom Interpolator.
void GrassInstance_float(
    float3 positionOS,
    float3 normalOS,
    float instanceID,
    out float3 PositionOut,
    out float3 NormalOut,
    out float Variation,
    out float SliceIndex,
    out float3 GroundTint)
{
#ifdef SHADERGRAPH_PREVIEW
    PositionOut = positionOS;
    NormalOut = normalOS;
    Variation = 0.5f;
    SliceIndex = 0.0f;
    GroundTint = 1.0f.xxx;
#else
    uint packedTint = _GrassTints[(uint) instanceID];

    // Stored sRGB-encoded, like a texture. The Base Color block wants linear, and the
    // texture sampler is already converting the greyscale art for us, so this has to make
    // the same trip or the tint comes out noticeably washed out.
    float3 tintSRGB = float3(
        (packedTint & 0xFFu) / 255.0f,
        ((packedTint >> 8) & 0xFFu) / 255.0f,
        ((packedTint >> 16) & 0xFFu) / 255.0f);

    GroundTint = GrassSRGBToLinear(tintSRGB);

    float4x4 m = _GrassInstances[(uint) instanceID];

    // Basis vectors of the instance transform. Their lengths are the per-axis
    // scales, which are no longer uniform: X and Z carry width, Y carries height.
    float3 axisX = float3(m._m00, m._m10, m._m20);
    float3 axisY = float3(m._m01, m._m11, m._m21);
    float3 axisZ = float3(m._m02, m._m12, m._m22);
    float3 pivotWS = float3(m._m03, m._m13, m._m23);

    // Wind amplitude follows the blade's HEIGHT, not its width - a tall thin blade
    // and a short wide one should not sway by the same distance.
    float heightScale = length(axisY);

    Variation = GrassHash13(pivotWS);

    // Deliberately a DIFFERENT hash to the tint variation. Sharing one would make every
    // instance of a given mesh variant the same colour, which reads as a repeating pattern
    // rather than as variety.
    float sliceCount = max(_GrassVariantParams.x, 1.0f);
    float variantRand = GrassHash13(pivotWS + 17.13f);
    SliceIndex = min(floor(variantRand * sliceCount), sliceCount - 1.0f);

    // 0 at the base, 1 at the tip, squared so the root stays planted.
    float bend = saturate(positionOS.y / max(_GrassFadeParams.z, 1e-4f));
    bend *= bend;

    // Per-instance phase offset on top of the world-space wave, so neighbouring
    // blades of different heights do not move in lockstep.
    float phase = dot(pivotWS.xz, _GrassWindParams.xy) * _GrassWindParams2.x
                + Variation * 6.2831853f * _GrassWindParams2.z;
    float t = _Time.y * _GrassWindParams.w;

    float sway = sin(t + phase);
    // Slower second harmonic so the field does not read as one sine wave.
    float gust = sin(t * 0.37f + phase * 0.63f) * _GrassWindParams2.y;

    float amount = (sway + gust) * _GrassWindParams.z * bend * heightScale;

    // Absolute world position of this vertex. Wind is added here, after the
    // transform, so every blade leans the same way regardless of its random yaw.
    float3 positionAWS = mul(m, float4(positionOS, 1.0f)).xyz;
    positionAWS += float3(_GrassWindParams.x, 0.0f, _GrassWindParams.y) * amount;

    // Rotation only: normalising the basis vectors strips the now non-uniform
    // width/height scale, which would otherwise skew the normal.
    float3x3 rotation = float3x3(
        normalize(axisX),
        normalize(axisY),
        normalize(axisZ));
    float3 normalAWS = normalize(mul(transpose(rotation), normalOS));

    // Bend the normal toward world up.
    //
    // A grass card is a flat quad, so every one of its pixels shares one horizontal normal
    // and the whole card lights uniformly - which is what makes an unbent field read as
    // flat cut-outs, and dark, since a sideways-facing surface catches far less sky and sun
    // than the ground beside it. Leaning the normal upward makes a field of cards shade
    // like a soft mass instead, and picks up the same light the terrain does.
    //
    // Requires Double-Sided Normal Mode = None on the material. Mirror and Flip negate the
    // normal on back faces, which would point these downward and render half the blades
    // black.
    normalAWS = normalize(lerp(normalAWS, float3(0.0f, 1.0f, 0.0f), saturate(_GrassVariantParams.y)));

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

// Shifts the grass texture's colour toward the ground it stands on, without flattening it.
//
// A MULTIPLY cannot do this job. Multiplying painted green art by a yellow ground colour
// only darkens the green - it desaturates toward grey rather than actually arriving at
// yellow, because multiply can never add a hue the texture does not already contain. That
// is why the earlier ratio tint never really conformed to the terrain.
//
// Instead: every painted detail in the art - blade separation, darker roots, lighter tips,
// the differences between clumps - is carried by LUMINANCE. Re-expressing that same
// luminance in the ground's hue moves the colour wherever the ground is, while leaving
// every one of those details exactly intact. Then blend by taste.
void GrassTintAlbedo_float(float3 albedo, float3 groundTint, out float3 Out)
{
    Out = albedo;

#ifndef SHADERGRAPH_PREVIEW
    float strength = saturate(_GrassVariantParams.z);
    if (strength <= 0.0f)
        return;

    const float3 lumWeights = float3(0.2126f, 0.7152f, 0.0722f);

    float albedoLum = dot(albedo, lumWeights);
    float tintLum = max(dot(groundTint, lumWeights), 1e-4f);

    float3 recoloured = groundTint * (albedoLum / tintLum);
    Out = lerp(albedo, recoloured, strength);
#endif
}

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