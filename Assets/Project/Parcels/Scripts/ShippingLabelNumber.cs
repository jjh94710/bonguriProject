using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class ShippingLabelNumber : MonoBehaviour
{
    public enum LabelCode { [InspectorName("10")] Code10 = 10, [InspectorName("20")] Code20 = 20, [InspectorName("30")] Code30 = 30 }
    [SerializeField] LabelCode code = LabelCode.Code10;
    [SerializeField] bool randomizeOnStart;
    [SerializeField] TextMesh numberText;
    [SerializeField] TextMesh regionText;
    [SerializeField, HideInInspector] float maxWidth = .028f, maxHeight = .010f;
    [SerializeField, HideInInspector] float regionWidth = .030f, regionHeight = .0045f;
    public int Code => (int)code;
    public TextMesh NumberText => numberText;
    public TextMesh RegionText => regionText;
    public string RegionName => code == LabelCode.Code20 ? "경기도" : code == LabelCode.Code30 ? "충청도" : "서울특별시";
    void OnEnable() { Refresh(); }
    void Start()
    {
        if (Application.isPlaying && randomizeOnStart) code = (LabelCode)((Random.Range(0, 3) + 1) * 10);
        Refresh();
    }
    void LateUpdate() { Refresh(); }
    public void Configure(TextMesh text, float width, float height)
    { numberText = text; maxWidth = width; maxHeight = height; Refresh(); }
    public void ConfigureRegion(TextMesh text, float width, float height)
    { regionText = text; regionWidth = width; regionHeight = height; Refresh(); }
    public bool SetCode(int value)
    {
        if (value != 10 && value != 20 && value != 30) return false;
        code = (LabelCode)value; Refresh(); return true;
    }
    public void Refresh()
    {
        if (code != LabelCode.Code10 && code != LabelCode.Code20 && code != LabelCode.Code30) code = LabelCode.Code10;
        UpdateText(numberText, ((int)code).ToString(), maxWidth, maxHeight);
        UpdateText(regionText, RegionName, regionWidth, regionHeight);
    }
    static void UpdateText(TextMesh text, string value, float width, float height)
    {
        if (text == null) return;
        if (text.text != value) text.text = value;
        var renderer = text.GetComponent<MeshRenderer>();
        if (renderer == null) return;
        var size = renderer.localBounds.size;
        if (size.x < .000001f || size.y < .000001f) return;
        var scale = Vector3.one * Mathf.Min(width / size.x, height / size.y);
        if ((text.transform.localScale - scale).sqrMagnitude > 1e-12f) text.transform.localScale = scale;
    }
}
