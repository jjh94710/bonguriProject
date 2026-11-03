using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SideCameraNavigation : MonoBehaviour
{
    [SerializeField] Camera[] views = new Camera[3];
    [SerializeField] Camera[] otherViewCameras = Array.Empty<Camera>();
    [SerializeField] Button leftButton;
    [SerializeField] Button rightButton;
    public int CurrentViewIndex { get; private set; }
    public string CurrentViewName => views != null && CurrentViewIndex >= 0 && CurrentViewIndex < views.Length && views[CurrentViewIndex] != null ? views[CurrentViewIndex].name : "";
    bool[] initialCameraStates, initialListenerStates;
    Camera[] managed;
    int[] initialDisplays;
    int outputDisplay;
    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (views == null || views.Length == 0 || Array.Exists(views, c => c == null) || leftButton == null || rightButton == null)
        { Debug.LogError("[SideCameraNavigation] 카메라와 좌우 버튼을 연결하세요.", this); return; }
        var others = otherViewCameras ?? Array.Empty<Camera>();
        managed = new Camera[views.Length + others.Length];
        Array.Copy(views, managed, views.Length); Array.Copy(others, 0, managed, views.Length, others.Length);
        outputDisplay = views[0].targetDisplay;
        initialDisplays = new int[managed.Length];
        initialCameraStates = new bool[managed.Length]; initialListenerStates = new bool[managed.Length];
        for (int i = 0; i < managed.Length; i++)
        {
            if (managed[i] == null) continue;
            initialCameraStates[i] = managed[i].enabled;
            initialDisplays[i] = managed[i].targetDisplay;
            if (Array.IndexOf(views, managed[i]) >= 0) managed[i].targetDisplay = outputDisplay;
            var listener = managed[i].GetComponent<AudioListener>();
            initialListenerStates[i] = listener != null && listener.enabled;
        }
        leftButton.onClick.AddListener(NextView);
        rightButton.onClick.AddListener(PreviousView);
        SelectView(0);
    }
    public void NextView()
    {
        if (!Application.isPlaying || views == null || CurrentViewIndex >= views.Length - 1) return;
        SelectView(CurrentViewIndex + 1);
    }
    public void PreviousView()
    {
        if (!Application.isPlaying || CurrentViewIndex <= 0) return;
        SelectView(CurrentViewIndex - 1);
    }
    public void SelectView(int index)
    {
        if (!Application.isPlaying || managed == null || index < 0 || index >= views.Length) return;
        CurrentViewIndex = index;
        for (int i = 0; i < managed.Length; i++)
        {
            var camera = managed[i]; if (camera == null) continue;
            bool selected = camera == views[index]; camera.enabled = selected;
            var listener = camera.GetComponent<AudioListener>(); if (listener != null) listener.enabled = selected;
        }
        leftButton.interactable = index < views.Length - 1;
        rightButton.interactable = index > 0;
        var canvas = GetComponent<Canvas>(); if (canvas != null) canvas.targetDisplay = outputDisplay;
    }
    void OnDisable()
    {
        if (leftButton != null) leftButton.onClick.RemoveListener(NextView);
        if (rightButton != null) rightButton.onClick.RemoveListener(PreviousView);
        if (managed == null) return;
        for (int i = 0; i < managed.Length; i++)
        {
            if (managed[i] == null) continue;
            managed[i].enabled = initialCameraStates[i];
            managed[i].targetDisplay = initialDisplays[i];
            var listener = managed[i].GetComponent<AudioListener>(); if (listener != null) listener.enabled = initialListenerStates[i];
        }
        managed = null;
    }
}

