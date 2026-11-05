using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AlarmUI : MonoBehaviour
{
    [SerializeField] CellController controller;
    [SerializeField] GameObject popup;
    [SerializeField] TMP_Text heading, banner, eventsText;
    [SerializeField] Button closeButton;
    readonly Queue<string> events = new Queue<string>();
    public bool IsVisible => popup != null && popup.activeSelf;

    void Awake() { if (popup != null) popup.SetActive(false); }
    void OnEnable()
    {
        if (controller != null) controller.OnAlarm += Receive;
        if (closeButton != null) closeButton.onClick.AddListener(Dismiss);
    }
    void OnDisable()
    {
        if (controller != null) controller.OnAlarm -= Receive;
        if (closeButton != null) closeButton.onClick.RemoveListener(Dismiss);
    }
    void Update()
    {
        // OnAlarm fires before CellController updates its stopped state.
        if (IsVisible && heading != null)
            heading.text = controller != null && controller.IsAlarmActive ? "알람 · 공정 정지" : "경고 · 이벤트";
    }
    void Receive(string type, string boxId)
    {
        string message = string.IsNullOrWhiteSpace(type) ? "공정 경고" : type;
        string id = string.IsNullOrWhiteSpace(boxId) ? "" : " · " + boxId;
        events.Enqueue(System.DateTime.Now.ToString("HH:mm:ss") + "  " + message + id);
        while (events.Count > 5) events.Dequeue();
        if (banner != null) banner.text = message + id;
        if (eventsText != null) eventsText.text = string.Join("\n", events);
        if (popup != null) popup.SetActive(true);
    }
    // Closing the notification never clears an alarm or resumes machinery.
    public void Dismiss() { if (popup != null) popup.SetActive(false); }
}
