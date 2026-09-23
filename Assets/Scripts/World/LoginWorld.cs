using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;

/// <summary>
/// Port of stock <c>LoginWorld_c</c> (GUI.dll, ctor 100160c5).
///
/// Stock's login backdrop is not a playfield. The constructor loops four times, building one
/// stage object per iteration (44 bytes each, held in a vector at <c>this+0x20</c>), and adds
/// the *same five* meshes to every stage:
///
///     AddMesh(this, stage, "charactercreation_main.abiff");
///     AddMesh(this, stage, "charactercreation_professions.abiff");
///     AddMesh(this, stage, "charactercreation_professions02.abiff");
///     AddMesh(this, stage, "charactercreation_nanoeffect.abiff");
///     AddMesh(this, stage, "charactercreation_adventurer.abiff");
///
/// Each stage then stores a pose: a position at offset 0 and a quaternion at 0xc, both
/// initialised from the same constants for all four stages. (<c>charactercreation_seq01.abiff</c>
/// exists in the binary but is not added here, so something else stages it.)
///
/// The camera is built separately and is *not* placed by those constants:
/// <c>VisualCamera_t(1.0471976, displayWidth/displayHeight, 0.5, 1000.0)</c> — a 60 degree
/// vertical FOV with near 0.5 and far 1000. The stage pose is what a stage is moved to.
/// </summary>
public sealed class LoginWorld : MonoBehaviour
{
    /// <summary>Stock's stage count: the ctor loop runs for stage 0..3.</summary>
    public const int StageCount = 4;

    /// <summary>Stock: <c>1.0471976</c> rad, i.e. 60 degrees.</summary>
    public const float CameraFieldOfViewDegrees = 60f;
    public const float CameraNearClip = 0.5f;
    public const float CameraFarClip = 1000f;

    /// <summary>The five meshes stock adds to every stage, in ctor order.</summary>
    public static readonly string[] StageMeshNames =
    {
        "charactercreation_main.abiff",
        "charactercreation_professions.abiff",
        "charactercreation_professions02.abiff",
        "charactercreation_nanoeffect.abiff",
        "charactercreation_adventurer.abiff"
    };

    [Inject] AbiffLoader _abiffLoader;
    [Inject] AbiffMeshNames _meshNames;

    [Header("Camera")]
    [SerializeField] Camera _camera;

    // Component order is xyzw, not wxyz: reading it w-first yields up=(-0.21,-0.98,-0.00), an
    // upside-down camera. As xyzw the pose is up=(0,+0.98,-0.21), forward=(0.01,-0.21,-0.98) —
    // looking -Z from y=2.27 with a 12 degree downward tilt, i.e. framing a character.
    [Header("Stage pose (stock ctor constants, identical for all four stages)")]
    [SerializeField] Vector3 _stagePosition = new(0.6577f, 2.2745f, -10.549f);
    [Tooltip("Stored xyzw at stage+0xc.")]
    [SerializeField] Vector4 _stageRotation = new(0.00049f, -0.99452f, 0.10480f, -0.005757f);

    readonly List<GameObject> _visuals = new();
    readonly Transform[] _stages = new Transform[StageCount];
    readonly Vector3[] _stageCameraPositions = new Vector3[StageCount];
    readonly Quaternion[] _stageCameraRotations = new Quaternion[StageCount];
    readonly bool[] _stageBuilt = new bool[StageCount];
    readonly List<int> _meshIds = new();

    int _stage;

    public int Stage => _stage;

    /// <summary>
    /// The active stage's root. Do not index <c>transform.GetChild(stage)</c> — the login
    /// camera is also a child, so child order does not match stage order.
    /// </summary>
    public Transform CurrentStage => _stage >= 0 && _stage < StageCount ? _stages[_stage] : null;

    public Transform StageRoot(int stage) => stage >= 0 && stage < StageCount ? _stages[stage] : null;

    void Awake()
    {
        // Its own camera, not Camera.main: the gameplay N3Camera owns that one and
        // rewrites it every frame, so a pose set here would be overwritten immediately.
        // Stock does the same — LoginWorld_c news up its own VisualCamera_t.
        if (_camera == null)
        {
            var camGo = new GameObject("LoginCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.depth = 10f;                       // draw over the gameplay camera
        }
    }


    void Start()
    {
        ApplyCameraSettings();
        BuildStages();
        SetStage(0);
    }

    void OnDestroy()
    {
        foreach (GameObject go in _visuals)
            if (go != null)
                Destroy(go);

        _visuals.Clear();
    }

    /// <summary>Stock builds the camera from the display aspect; Unity derives that itself.</summary>
    public void ApplyCameraSettings()
    {
        if (_camera == null)
        {
            Debug.LogWarning("[LoginWorld] No camera; skipping login camera setup.");
            return;
        }

        _camera.fieldOfView = CameraFieldOfViewDegrees;
        _camera.nearClipPlane = CameraNearClip;
        _camera.farClipPlane = CameraFarClip;
    }

    /// <summary>Four stages, each holding the same five meshes and the same initial pose.</summary>
    void BuildStages()
    {
        // Stock addresses these by name; AbiffMeshNames maps them through InfoObject(1).
        _meshIds.Clear();
        foreach (string meshName in StageMeshNames)
        {
            if (_meshNames != null && _meshNames.TryResolve(meshName, out int id))
                _meshIds.Add(id);
            else
                Debug.LogWarning($"[LoginWorld] Could not resolve stage mesh '{meshName}'.");
        }

        var rotation = new Quaternion(_stageRotation.x, _stageRotation.y, _stageRotation.z, _stageRotation.w);

        for (int i = 0; i < StageCount; i++)
        {
            // The stage transform stays at identity. Stock's stage pose is CAMERA data
            // (stage+0x00 position, stage+0x0c rotation, read by SetStage) — the meshes sit at
            // their own world coordinates and LoginWorld_c::Show only toggles their visibility.
            // Parenting the geometry under the pose double-transforms the whole world.
            var stage = new GameObject($"Stage{i}").transform;
            stage.SetParent(transform, false);
            stage.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            stage.gameObject.SetActive(false);
            _stages[i] = stage;

            _stageCameraPositions[i] = _stagePosition;
            _stageCameraRotations[i] = rotation;
        }
    }

    /// <summary>
    /// Stock adds the same five meshes to all four stages up front. Doing that literally means
    /// four identical copies of ~220 renderers each when only one stage is ever visible, so the
    /// geometry is built on a stage's first activation instead. Same result, a quarter of the
    /// load.
    /// </summary>
    void EnsureStageBuilt(int stage)
    {
        if (_stageBuilt[stage] || _stages[stage] == null)
            return;

        _stageBuilt[stage] = true;
        foreach (int meshId in _meshIds)
            TryAddMesh(meshId, _stages[stage]);
    }

    /// <summary>Activates one stage and moves the camera onto its pose.</summary>
    public void SetStage(int stage)
    {
        if (stage < 0 || stage >= StageCount)
        {
            Debug.LogWarning($"[LoginWorld] Stage {stage} is out of range 0..{StageCount - 1}.");
            return;
        }

        _stage = stage;
        EnsureStageBuilt(stage);

        for (int i = 0; i < StageCount; i++)
            if (_stages[i] != null)
                _stages[i].gameObject.SetActive(i == stage);

        // Stock: VisualCamera_t::SetPosition(stages[n]) / SetRotation(stages[n]+0xc).
        if (_camera != null)
            _camera.transform.SetPositionAndRotation(_stageCameraPositions[stage], _stageCameraRotations[stage]);
    }

    void TryAddMesh(int meshId, Transform parent)
    {
        if (meshId == 0 || _abiffLoader == null)
            return;

        if (_abiffLoader.TryCreateVisual(meshId, parent, out GameObject visual) && visual != null)
            _visuals.Add(visual);
        else
            Debug.LogWarning($"[LoginWorld] Failed to create stage mesh {meshId}.");
    }
}
