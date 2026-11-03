using UnityEngine;

/// <summary>
/// FR5 + 2-finger gripper controller for the URDF-Importer result.
/// Unity ignores URDF &lt;mimic&gt;, so this script drives both finger joints together.
/// Attach to the imported robot root (fr5_with_gripper).
/// 0 = fully open, 0.04 m = fully closed (per finger).
/// </summary>
public class GripperController : MonoBehaviour
{
    [Range(0f, 0.04f)] public float closeAmount = 0f;   // meters per finger
    public float stiffness = 10000f;
    public float damping = 100f;
    public float forceLimit = 140f;

    ArticulationBody right, left;

    void Awake()
    {
        foreach (var ab in GetComponentsInChildren<ArticulationBody>())
        {
            if (ab.name == "finger_right_link") right = ab;
            else if (ab.name == "finger_left_link") left = ab;
        }
        if (right == null || left == null)
            Debug.LogError("GripperController: finger_right_link / finger_left_link not found.");
    }

    void FixedUpdate()
    {
        SetTarget(right, closeAmount);
        SetTarget(left, closeAmount);
    }

    void SetTarget(ArticulationBody ab, float target)
    {
        if (ab == null) return;
        var d = ab.xDrive;
        d.stiffness = stiffness;
        d.damping = damping;
        d.forceLimit = forceLimit;
        d.target = target;            // prismatic drive target is in meters
        ab.xDrive = d;
    }

    [ContextMenu("Open")]  public void Open()  => closeAmount = 0f;
    [ContextMenu("Close")] public void Close() => closeAmount = 0.04f;
}
