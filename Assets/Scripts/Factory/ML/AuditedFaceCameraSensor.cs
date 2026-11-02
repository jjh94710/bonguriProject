using System;
using Unity.MLAgents.Sensors;

/// <summary>실제 학습 센서가 Update에서 받은 프레임을 그대로 복사한다. 재촬영하지 않는다.</summary>
public class AuditedFaceCameraSensor : CameraSensorComponent
{
    public int RequestedSample { get; private set; } = -1;
    public int CapturedSample { get; private set; } = -1;
    public byte[] CapturedPng { get; private set; }
    public void RequestCapture(int sample)
    {
        RequestedSample = sample;
        CapturedSample = -1;
        CapturedPng = null;
    }
    public override ISensor[] CreateSensors()
    {
        var sensors = base.CreateSensors();
        if (sensors.Length != 1 || ObservationStacks != 1)
            throw new InvalidOperationException("면 판정 검증은 센서당 단일 프레임을 사용합니다.");
        return new ISensor[] { new AuditSensor(sensors[0], this) };
    }
    sealed class AuditSensor : ISensor, IDisposable
    {
        readonly ISensor inner;
        readonly AuditedFaceCameraSensor owner;
        public AuditSensor(ISensor sensor, AuditedFaceCameraSensor component) { inner = sensor; owner = component; }
        public void Update()
        {
            inner.Update();
            if (owner.RequestedSample < 0 || owner.CapturedSample == owner.RequestedSample) return;
            owner.CapturedPng = inner.GetCompressedObservation();
            owner.CapturedSample = owner.RequestedSample;
        }
        public ObservationSpec GetObservationSpec() => inner.GetObservationSpec();
        public int Write(ObservationWriter writer) => inner.Write(writer);
        public byte[] GetCompressedObservation() => inner.GetCompressedObservation();
        public void Reset() => inner.Reset();
        public CompressionSpec GetCompressionSpec() => inner.GetCompressionSpec();
        public string GetName() => inner.GetName();
        public void Dispose() { if (inner is IDisposable disposable) disposable.Dispose(); }
    }
}
