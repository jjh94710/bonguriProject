using UnityEngine;

namespace RoboLacyTech.Conveyors
{
    public enum ConveyorAxis
    {
        Up,
        Forward,
        Right,
        Left
    }

    public enum ConveyorDirection
    {
        Forward  =  1,
        Backward = -1
    }

    [System.Serializable]
    public class ConveyorMotor
    {
        [SerializeField, Min(0f)] private float baseSpeed = 1f;
        [SerializeField] private ConveyorDirection direction = ConveyorDirection.Forward;
        [SerializeField] private bool startRunning = true;
        [SerializeField] private bool useAcceleration = true;
        [SerializeField, Min(0f)] private float acceleration = 2f;
        [SerializeField, Min(0f)] private float deceleration = 4f;

        private float currentSpeed;
        private bool isRunning;
        private bool initialized;

        public float BaseSpeed
        {
            get => baseSpeed;
            set => baseSpeed = Mathf.Max(0f, value);
        }

        public ConveyorDirection Direction
        {
            get => direction;
            set => direction = value;
        }

        public float TargetSpeed  => isRunning ? baseSpeed * (int)direction : 0f;
        public float CurrentSpeed => currentSpeed;
        public bool  IsRunning    => isRunning;
        public bool  IsReversed   => direction == ConveyorDirection.Backward;

        public void Initialize()
        {
            if (initialized) return;
            isRunning    = startRunning;
            currentSpeed = useAcceleration ? 0f : TargetSpeed;
            initialized  = true;
        }

        public void Update(float deltaTime)
        {
            if (!initialized) Initialize();

            float target = TargetSpeed;

            if (useAcceleration)
            {
                bool decelerating = currentSpeed != 0f
                    && Mathf.Sign(currentSpeed) != Mathf.Sign(target);

                float rate = decelerating ? deceleration : acceleration;
                currentSpeed = Mathf.MoveTowards(currentSpeed, target, rate * deltaTime);
            }
            else
            {
                currentSpeed = target;
            }
        }

        public void SetDirection(ConveyorDirection newDirection) => direction = newDirection;

        public void Reverse() =>
            direction = direction == ConveyorDirection.Forward
                ? ConveyorDirection.Backward
                : ConveyorDirection.Forward;

        public void Play()   => isRunning = true;
        public void Stop()   => isRunning = false;
        public void Toggle() => isRunning = !isRunning;

        public void Reset()
        {
            currentSpeed = 0f;
            initialized  = false;
        }
    }
}
