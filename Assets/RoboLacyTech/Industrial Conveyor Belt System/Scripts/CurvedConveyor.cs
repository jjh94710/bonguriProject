using UnityEngine;
using UnityEngine.Events;

namespace RoboLacyTech.Conveyors
{
    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("RoboLacyTech/Conveyors/Curved Conveyor")]
    public class CurvedConveyor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private ConveyorMotor motor = new ConveyorMotor();
        [SerializeField] private ConveyorAxis curveAxis = ConveyorAxis.Up;

        [Header("Arc Shape")]
        [Tooltip("Center of the arc in local space.")]
        [SerializeField] private Vector3 arcOriginOffset = Vector3.zero;
        [SerializeField, Min(0.01f)] private float radius = 4f;
        [SerializeField, Range(1f, 360f)] private float curveAngle = 90f;

        [Header("Belt Visual")]
        [SerializeField] private Renderer beltRenderer;
        [SerializeField, Min(0.01f)] private float beltTextureLength = 1f;
        [SerializeField] private bool invertTextureDirection;

        [Header("Events")]
        [SerializeField] private UnityEvent onStarted;
        [SerializeField] private UnityEvent onStopped;
        [SerializeField] private UnityEvent onDirectionChanged;

        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexST  = Shader.PropertyToID("_MainTex_ST");

        private Rigidbody rb;
        private MaterialPropertyBlock propertyBlock;
        private int texSTID;
        private Vector2 baseTiling = Vector2.one;
        private float textureOffset;

        public ConveyorMotor Motor => motor;
        public bool IsRunning => motor.IsRunning;
        public ConveyorDirection Direction => motor.Direction;
        public float CurrentSpeed => motor.CurrentSpeed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            motor.Initialize();

            propertyBlock = new MaterialPropertyBlock();
            if (beltRenderer != null && beltRenderer.sharedMaterial != null)
            {
                Material mat = beltRenderer.sharedMaterial;
                texSTID = mat.HasProperty(BaseMapST) ? BaseMapST : MainTexST;
                if (mat.HasProperty(texSTID))
                {
                    Vector4 st = mat.GetVector(texSTID);
                    baseTiling = new Vector2(st.x, st.y);
                }
            }
        }

        private void FixedUpdate()
        {
            motor.Update(Time.fixedDeltaTime);
            float arcLength      = GetArcLength();
            float rotationAmount = curveAngle * motor.CurrentSpeed * Time.fixedDeltaTime / arcLength;

            Vector3    worldAxis         = GetAxisDirection();
            Quaternion originalRotation  = rb.rotation;

            Quaternion simulatedRotation = Quaternion.AngleAxis(rotationAmount, worldAxis);
            rb.rotation = simulatedRotation * originalRotation;
            rb.MoveRotation(originalRotation);

            UpdateBeltTexture(Time.fixedDeltaTime);
        }



        private Vector3 GetAxisDirection()
        {
            return curveAxis switch
            {
                ConveyorAxis.Up      => transform.up,
                ConveyorAxis.Forward => transform.forward,
                ConveyorAxis.Right   => transform.right,
                ConveyorAxis.Left    => -transform.right,
                _                    => transform.up
            };
        }

        private float GetArcLength() => 2f * Mathf.PI * radius * (curveAngle / 360f);

        private void UpdateBeltTexture(float deltaTime)
        {
            if (beltRenderer == null) return;

            float sign     = invertTextureDirection ? -1f : 1f;
            textureOffset += sign * motor.CurrentSpeed * deltaTime / beltTextureLength;

            beltRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector(texSTID, new Vector4(baseTiling.x, baseTiling.y, 0f, textureOffset));
            beltRenderer.SetPropertyBlock(propertyBlock);
        }
        /// <summary>Sets belt speed. Negative values switch to Backward direction.</summary>
        public void SetSpeed(float speed)
        {
            motor.BaseSpeed = Mathf.Abs(speed);
            if (speed != 0f)
                motor.SetDirection(speed > 0f ? ConveyorDirection.Forward : ConveyorDirection.Backward);
        }

        public void SetDirection(ConveyorDirection newDirection)
        {
            motor.SetDirection(newDirection);
            onDirectionChanged?.Invoke();
        }

        public void Reverse()
        {
            motor.Reverse();
            onDirectionChanged?.Invoke();
        }

        public void Play()
        {
            motor.Play();
            onStarted?.Invoke();
        }

        public void Stop()
        {
            motor.Stop();
            onStopped?.Invoke();
        }

        public void Toggle()
        {
            motor.Toggle();
            if (motor.IsRunning) onStarted?.Invoke();
            else                 onStopped?.Invoke();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 axisDir  = GetAxisDirection();
            Vector3 originWS = transform.TransformPoint(arcOriginOffset);

            Vector3 fromDir = Vector3.Cross(axisDir, transform.forward).normalized;
            if (fromDir.sqrMagnitude < 0.01f)
                fromDir = Vector3.Cross(axisDir, transform.right).normalized;

            bool running = Application.isPlaying ? motor.IsRunning : true;
            Color c = running
                ? (motor.Direction == ConveyorDirection.Forward
                    ? new Color(0.25f, 1f, 0.4f)
                    : new Color(1f, 0.55f, 0.1f))
                : Color.gray;

            UnityEditor.Handles.color = c;
            UnityEditor.Handles.DrawWireArc(originWS, axisDir, fromDir, curveAngle, radius);

            Vector3 startPt = originWS + fromDir * radius;
            Vector3 endPt   = originWS + Quaternion.AngleAxis(curveAngle, axisDir) * (fromDir * radius);
            Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
            Gizmos.DrawLine(originWS, startPt);
            Gizmos.DrawLine(originWS, endPt);

            Gizmos.color = new Color(c.r, c.g, c.b, 0.6f);
            Gizmos.DrawSphere(originWS, radius * 0.03f);

            float   midAngle   = curveAngle * 0.5f;
            Vector3 midRadial  = Quaternion.AngleAxis(midAngle, axisDir) * fromDir;
            Vector3 midPoint   = originWS + midRadial * radius;
            float   arrowSign  = motor.Direction == ConveyorDirection.Backward ? -1f : 1f;
            Vector3 tangent    = Vector3.Cross(axisDir, midRadial) * arrowSign;

            if (tangent.sqrMagnitude > 0.001f)
            {
                UnityEditor.Handles.color = c;
                UnityEditor.Handles.ArrowHandleCap(
                    0,
                    midPoint - tangent * 0.15f,
                    Quaternion.LookRotation(tangent),
                    0.35f,
                    EventType.Repaint);
            }
        }
#endif
    }
}
