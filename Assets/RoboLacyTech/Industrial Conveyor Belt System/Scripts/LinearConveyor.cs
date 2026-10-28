using UnityEngine;
using UnityEngine.Events;

namespace RoboLacyTech.Conveyors
{
    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("RoboLacyTech/Conveyors/Linear Conveyor")]
    public class LinearConveyor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private ConveyorMotor motor = new ConveyorMotor();
        [SerializeField] private ConveyorAxis movementAxis = ConveyorAxis.Forward;

        [Header("Belt Visual")]
        [SerializeField] private Renderer beltRenderer;
        [SerializeField, Min(0.01f)] private float beltTextureLength = 2f;
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

            Vector3 dir              = GetWorldDirection();
            Vector3 originalPosition = rb.position;
            rb.position += dir * motor.CurrentSpeed * Time.fixedDeltaTime;
            rb.MovePosition(originalPosition);

            UpdateBeltTexture(Time.fixedDeltaTime);
        }

        private Vector3 GetWorldDirection()
        {
            return movementAxis switch
            {
                ConveyorAxis.Up      => transform.up,
                ConveyorAxis.Forward => transform.forward,
                ConveyorAxis.Right   => transform.right,
                ConveyorAxis.Left    => -transform.right,
                _                    => transform.forward
            };
        }

        private void UpdateBeltTexture(float deltaTime)
        {
            if (beltRenderer == null) return;

            float sign     = invertTextureDirection ? 1f : -1f;
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
            float   dirSign = motor.Direction == ConveyorDirection.Backward ? -1f : 1f;
            Vector3 dir     = GetWorldDirection() * dirSign;
            if (dir.sqrMagnitude < 0.001f) return;

            bool running = Application.isPlaying ? motor.IsRunning : true;
            Color c = running
                ? (motor.Direction == ConveyorDirection.Forward
                    ? new Color(0.25f, 1f, 0.4f)
                    : new Color(1f, 0.55f, 0.1f))
                : Color.gray;

            Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
            Gizmos.DrawLine(transform.position - dir * 0.5f, transform.position + dir * 0.5f);

            UnityEditor.Handles.color = c;
            UnityEditor.Handles.ArrowHandleCap(
                0,
                transform.position - dir * 0.2f,
                Quaternion.LookRotation(dir),
                0.55f,
                EventType.Repaint);
        }
#endif
    }
}
